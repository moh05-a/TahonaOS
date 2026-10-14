using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Application.Common;
using Tahona.Application.Features.Import;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Services;
using static Tahona.Application.Features.Import.ImportDefinitions;

namespace Tahona.Tests;

/// <summary>Reading source files, and the import cycle: dry run, commit, retry, duplicates, undo and failure.</summary>
public class ImportTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static ImportRunRequest Request(ImportUploadDto upload, ImportOptions? options = null, Dictionary<int, string>? decisions = null) =>
        new(upload.SuggestedMapping, options, decisions);

    // ------------------------------------------------------------------ reading files

    [Fact]
    public void Csv_handles_quotes_line_breaks_semicolons_and_blank_lines()
    {
        var file = CsvText.Parse("﻿Name;Phone;Notes\r\n\"Khalil; Ahmad\";0791234567;\"Said \"\"call later\"\"\nsecond line\"\r\n\r\nSara;;\r\n");

        Assert.Equal(["Name", "Phone", "Notes"], file.Headers);
        Assert.Equal(2, file.Rows.Count);
        Assert.Equal("Khalil; Ahmad", file.Rows[0][0]);
        Assert.Equal("Said \"call later\"\nsecond line", file.Rows[0][2]);
        Assert.Equal(["Sara", "", ""], file.Rows[1]);
    }

    [Fact]
    public void Csv_names_blank_and_repeated_headers_and_pads_short_rows()
    {
        var file = CsvText.Parse("Name,,Phone,Phone,,\nAhmad,x,1,2\n");
        Assert.Equal(["Name", "Column 2", "Phone", "Phone (2)"], file.Headers);
        Assert.Equal(["Ahmad", "x", "1", "2"], file.Rows[0]);

        Assert.Throws<RequestValidationException>(() => CsvText.Parse("Name,Phone\n\"unterminated,1\n"));
        Assert.Throws<RequestValidationException>(() => CsvText.Parse("   \n"));
    }

    [Fact]
    public void Arabic_files_are_read_whether_saved_as_utf8_or_windows_1256()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string text = "الاسم,رقم الهاتف\nأحمد خليل,0791234567\n";
        var reader = new TabularFileReader();

        foreach (var bytes in new[] { Encoding.UTF8.GetBytes(text), Encoding.GetEncoding(1256).GetBytes(text) })
        {
            var file = reader.Read(new MemoryStream(bytes), "patients.csv");
            Assert.Equal("الاسم", file.Headers[0]);
            Assert.Equal("أحمد خليل", file.Rows[0][0]);
        }
        Assert.Throws<RequestValidationException>(() => reader.Read(Csv("x"), "patients.pdf"));
        Assert.Throws<RequestValidationException>(() => reader.Read(Csv("not a workbook"), "patients.xlsx"));
    }

    [Fact]
    public void Exported_csv_cannot_run_formulas_and_reads_back_unchanged()
    {
        var bytes = CsvText.Write([["Name", "Note"], ["=HYPERLINK(\"http://x\")", "+962791234567"], ["Sara, Jr.", "said \"hi\""]]);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("﻿", text);
        Assert.Contains("'=HYPERLINK", text);
        var back = CsvText.Parse(text);
        Assert.Equal("Sara, Jr.", back.Rows[1][0]);
        Assert.Equal("said \"hi\"", back.Rows[1][1]);
        Assert.Equal("+962791234567", ImportParsing.Normalize(back.Rows[0][1]));
    }

    [Theory]
    [InlineData("14/03/1988", "dmy", "1988-03-14")]
    [InlineData("3/4/2025", "dmy", "2025-04-03")]
    [InlineData("3/4/2025", "mdy", "2025-03-04")]
    [InlineData("2025-04-03", "mdy", "2025-04-03")]       // year-first is never ambiguous
    [InlineData("١٤/٠٣/١٩٨٨", "dmy", "1988-03-14")]        // Arabic-Indic digits
    [InlineData("14-Mar-2025", "dmy", "2025-03-14")]
    [InlineData("2025-04-03T00:00:00", "dmy", "2025-04-03")]
    public void Dates_are_read_in_the_chosen_order(string input, string order, string expected) =>
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd"), ImportParsing.Date(input, order));

    [Theory]
    [InlineData("31/02/2025")]
    [InlineData("13/13/2025")]
    [InlineData("tomorrow")]
    [InlineData("14/03/0025")]
    public void Unreadable_dates_are_not_guessed(string input) => Assert.Null(ImportParsing.Date(input));

    [Theory]
    [InlineData("120.500", 120.5)]
    [InlineData("1,250.500", 1250.5)]
    [InlineData("1250,5", 1250.5)]
    [InlineData("1.250,50", 1250.5)]
    [InlineData("1,250", 1250)]
    [InlineData("١٢٠٫٥ د.أ", 120.5)]
    [InlineData("JOD 75", 75)]
    [InlineData("-40", -40)]
    [InlineData("(40)", -40)]
    public void Amounts_are_read_in_the_formats_clinics_use(string input, double expected) =>
        Assert.Equal((decimal)expected, ImportParsing.Amount(input));

    [Fact]
    public void Times_words_and_blanks_are_parsed_or_left_alone()
    {
        Assert.Equal(new TimeOnly(16, 30), ImportParsing.Time("16:30"));
        Assert.Equal(new TimeOnly(16, 30), ImportParsing.Time("4:30 PM"));
        Assert.Equal(new TimeOnly(9, 0), ImportParsing.Time("9:00 ص"));
        Assert.Equal(new TimeOnly(14, 30), ImportParsing.Time("14:30:00.0000000"));
        Assert.Null(ImportParsing.Time("afternoon"));
        Assert.Null(ImportParsing.Amount("n/a"));

        Assert.Equal(Gender.Female, ImportParsing.GenderOf("أنثى"));
        Assert.Equal(Gender.Male, ImportParsing.GenderOf("M"));
        Assert.Null(ImportParsing.GenderOf("?"));
        Assert.Equal(AppointmentStatus.NoShow, ImportParsing.AppointmentStatusOf("لم يحضر"));
        Assert.Equal(AppointmentStatus.Completed, ImportParsing.AppointmentStatusOf("Done"));
        Assert.True(ImportParsing.YesNo("نعم"));
        Assert.True(ImportParsing.SameName("  AHMAD  khalil", "Ahmad Khalil"));
        Assert.True(ImportParsing.SameName("أحمد خليل", "احمد خليل"));
        Assert.False(ImportParsing.SameName("", ""));
    }

    [Fact]
    public void Columns_are_matched_from_english_and_arabic_headers()
    {
        var english = SuggestMapping(Patients, ["Patient Name", "Mobile Number", "DOB", "Sex", "File No", "Something else"]);
        Assert.Equal("Patient Name", english[F.FullName]);
        Assert.Equal("Mobile Number", english[F.Phone]);
        Assert.Equal("DOB", english[F.DateOfBirth]);
        Assert.Equal("Sex", english[F.Gender]);
        Assert.Equal("File No", english[F.FileNumber]);
        Assert.DoesNotContain("Something else", english.Values);

        var arabic = SuggestMapping(Patients, ["اسم المريض", "رقم الجوال", "تاريخ الميلاد", "الجنس", "رقم الملف"]);
        Assert.Equal("اسم المريض", arabic[F.FullName]);
        Assert.Equal("رقم الجوال", arabic[F.Phone]);
        Assert.Equal("تاريخ الميلاد", arabic[F.DateOfBirth]);
        Assert.Equal("رقم الملف", arabic[F.FileNumber]);

        // A header is never used for two fields.
        var used = english.Values.Where(v => v is not null).ToList();
        Assert.Equal(used.Count, used.Distinct().Count());
    }

    [Fact]
    public void Templates_carry_every_field_and_their_example_row_is_refused_on_import()
    {
        foreach (var definition in All)
        {
            var template = CsvText.Parse(Encoding.UTF8.GetString(Template(definition.Kind, "en")));
            Assert.Equal(definition.Fields.Select(f => f.Label), template.Headers);
            Assert.Single(template.Rows);
            var arabic = CsvText.Parse(Encoding.UTF8.GetString(Template(definition.Kind, "ar")));
            Assert.Equal(definition.Fields.Select(f => f.LabelAr), arabic.Headers);
            // Importing the template unchanged maps every column back to its own field.
            var mapping = SuggestMapping(definition, template.Headers);
            Assert.All(definition.Fields, f => Assert.Equal(f.Label, mapping[f.Key]));
        }
    }

    // ------------------------------------------------------------------ Excel

    /// <summary>Builds a minimal .xlsx: a blank first row, a header row, then typed cells (text, a number, a date).</summary>
    private static MemoryStream Workbook()
    {
        var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            Add("[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """.Trim());
            Add("_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """.Trim());
            Add("xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="المرضى" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """.Trim());
            Add("xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """.Trim());
            Add("xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font/></fonts><fills count="1"><fill/></fills><borders count="1"><border/></borders>
                  <cellStyleXfs count="1"><xf/></cellStyleXfs>
                  <cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14" applyNumberFormat="1"/></cellXfs>
                </styleSheet>
                """.Trim());
            Add("xl/worksheets/sheet1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                  <row r="2">
                    <c r="A2" t="inlineStr"><is><t>الاسم</t></is></c><c r="B2" t="inlineStr"><is><t>رقم الجوال</t></is></c>
                    <c r="C2" t="inlineStr"><is><t>تاريخ الميلاد</t></is></c>
                  </row>
                  <row r="3">
                    <c r="A3" t="inlineStr"><is><t>أحمد خليل</t></is></c><c r="B3"><v>791234567</v></c><c r="C3" s="1"><v>32216</v></c>
                  </row>
                </sheetData></worksheet>
                """.Trim());
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task An_excel_workbook_imports_with_typed_dates_and_numeric_phone_numbers()
    {
        var file = new TabularFileReader().Read(Workbook(), "patients.xlsx");
        Assert.Equal(["الاسم", "رقم الجوال", "تاريخ الميلاد"], file.Headers);          // the blank first row is skipped
        Assert.Equal(["أحمد خليل", "791234567", "1988-03-14"], file.Rows.Single());

        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.Patients, "patients.xlsx", Workbook(), "Excel", None);
        await imports.CommitAsync(upload.Batch.Id, Request(upload), None);

        var patient = await app.Db.Patients.SingleAsync();
        Assert.Equal("أحمد خليل", patient.FullName);
        Assert.Equal("791234567", patient.PhoneNormalized);
        Assert.Equal(new DateOnly(1988, 3, 14), patient.DateOfBirth);
    }

    // ------------------------------------------------------------------ patients

    private const string PatientsCsv = """
        File No,Name,Arabic Name,Mobile,DOB,Gender,Allergies,Consent
        1001,Ahmad Khalil,أحمد خليل,0791234567,14/03/1988,Male,Penicillin,Yes
        1002,Sara Haddad,سارة حداد,+962 79 555 0002,02/11/1995,F,,
        1003,No Phone Person,,,01/01/1990,M,,
        1004,Bad Date,,0795550004,31/02/1990,?,,maybe
        1005,,,0795550005,,,,
        """;

    [Fact]
    public async Task Preview_changes_nothing_and_commit_imports_exactly_what_was_previewed()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();

        var upload = await imports.UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv), "Old System", None);
        Assert.Equal(5, upload.Batch.TotalRows);
        Assert.Equal("Name", upload.SuggestedMapping[F.FullName]);
        Assert.Equal("Mobile", upload.SuggestedMapping[F.Phone]);

        var preview = await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);
        Assert.Equal(3, preview.Summary.Ready);
        Assert.Equal(2, preview.Summary.Errors);
        Assert.Equal(3, preview.Summary.WillCreate);
        Assert.Empty(await app.Db.Patients.ToListAsync());                       // the dry run wrote no patient

        var noPhone = preview.Rows.Single(r => r.RowNumber == 3);
        Assert.Equal(RowVerdict.Error, noPhone.Verdict);
        Assert.Contains("phone number is missing", noPhone.Errors[0]);
        Assert.Contains("name is missing", preview.Rows.Single(r => r.RowNumber == 5).Errors[0]);
        var badDate = preview.Rows.Single(r => r.RowNumber == 4);
        Assert.Equal(RowVerdict.Ready, badDate.Verdict);
        Assert.Contains(badDate.Warnings, w => w.Contains("date of birth") && w.Contains("31/02/1990"));
        Assert.Contains(badDate.Warnings, w => w.Contains("Gender"));
        Assert.Contains(badDate.Warnings, w => w.Contains("consent"));

        var committed = await imports.CommitAsync(upload.Batch.Id, Request(upload), None);
        Assert.Equal(ImportBatchStatus.Committed, committed.Batch.Status);
        Assert.Equal(3, committed.Batch.CreatedCount);
        Assert.Equal(2, committed.Batch.ErrorCount);

        var ahmad = await app.Db.Patients.SingleAsync(p => p.FullName == "Ahmad Khalil");
        Assert.Equal("1001", ahmad.FileNumber);                                   // the clinic's own file number is kept
        Assert.Equal("1001", ahmad.ExternalId);
        Assert.Equal("Old System", ahmad.SourceSystem);
        Assert.Equal(committed.Batch.Id, ahmad.ImportBatchId);
        Assert.Equal("أحمد خليل", ahmad.FullNameAr);
        Assert.Equal(new DateOnly(1988, 3, 14), ahmad.DateOfBirth);
        Assert.Equal(Gender.Male, ahmad.Gender);
        Assert.Equal("791234567", ahmad.PhoneNormalized);
        Assert.Equal(MessagingConsent.Granted, ahmad.MessagingConsent);
        Assert.Equal("Import: Old System", ahmad.MessagingConsentSource);
        Assert.Null(ahmad.LastVisitAt);                                           // no visit was invented

        var bad = await app.Db.Patients.SingleAsync(p => p.FullName == "Bad Date");
        Assert.Null(bad.DateOfBirth);
        Assert.Null(bad.Gender);
        Assert.Equal(MessagingConsent.Unknown, bad.MessagingConsent);

        // Each source row records what became of it.
        var rows = await app.Db.ImportRows.OrderBy(r => r.RowNumber).ToListAsync();
        Assert.Equal([ImportRowStatus.Created, ImportRowStatus.Created, ImportRowStatus.Error, ImportRowStatus.Created, ImportRowStatus.Error], rows.Select(r => r.Status));
        Assert.Equal(ahmad.Id, rows[0].EntityId);
        Assert.Contains("phone number is missing", rows[2].MessagesJson);
    }

    [Fact]
    public async Task Committing_twice_or_uploading_the_same_file_again_creates_no_duplicates()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv), "Old System", None);
        await imports.CommitAsync(upload.Batch.Id, Request(upload), None);

        await Assert.ThrowsAsync<ConflictException>(() => imports.CommitAsync(upload.Batch.Id, Request(upload), None));
        await Assert.ThrowsAsync<ConflictException>(() => imports.PreviewAsync(upload.Batch.Id, Request(upload), None));
        await Assert.ThrowsAsync<BusinessRuleException>(() => imports.DeleteAsync(upload.Batch.Id, None));

        // The same export uploaded again (a retry, or a later full export with new patients added).
        var again = await imports.UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv + "\n1006,New Patient,,0795550006,,,,\n"), "Old System", None);
        var preview = await imports.PreviewAsync(again.Batch.Id, Request(again), None);
        Assert.Equal(3, preview.Summary.AlreadyImported);
        Assert.Equal(1, preview.Summary.WillCreate);

        await imports.CommitAsync(again.Batch.Id, Request(again), None);
        Assert.Equal(4, await app.Db.Patients.CountAsync());

        // An identical file is recognised as soon as it is uploaded.
        var identical = await imports.UploadAsync(ImportKind.Patients, "copy.csv", Csv(PatientsCsv), "Old System", None);
        Assert.NotNull(identical.PreviousImportAt);
    }

    [Fact]
    public async Task Duplicates_are_shown_for_a_decision_and_existing_details_are_never_overwritten()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var existing = await app.AddPatientAsync("Ahmad Khalil", "0791234567");
        existing.Allergies = "Latex";
        await app.AddPatientAsync("Mona Saleh", "0795550009");
        await app.Db.SaveChangesAsync();
        var imports = app.Get<ImportService>();

        const string csv = """
            Name,Mobile,Allergies,Email,Address
            ahmad  khalil,079 123 4567,Penicillin,ahmad@example.com,Abdoun
            Omar Saleh,0795550009,,,
            Omar Saleh,0795550009,,,
            Brand New,0795550010,,,
            """;
        var upload = await imports.UploadAsync(ImportKind.Patients, "p.csv", Csv(csv), null, None);
        var preview = await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);

        var exact = preview.Rows.Single(r => r.RowNumber == 1);
        Assert.Equal(RowVerdict.Duplicate, exact.Verdict);
        Assert.Equal(RowActions.Skip, exact.Action);                              // default: never merge on its own
        Assert.Equal(existing.Id, exact.MatchId);
        Assert.Contains(RowActions.FillBlanks, exact.AllowedActions);

        var family = preview.Rows.Single(r => r.RowNumber == 2);
        Assert.Equal(RowVerdict.PossibleDuplicate, family.Verdict);               // same phone, different name
        Assert.Equal(RowActions.Create, family.Action);
        Assert.Contains("Mona Saleh", family.MatchLabel);

        var repeated = preview.Rows.Single(r => r.RowNumber == 3);
        Assert.Equal(RowVerdict.Duplicate, repeated.Verdict);                     // repeated inside the file
        Assert.Equal("Row 2", repeated.MatchLabel);

        // The person decides: fill the empty fields of the existing patient from row 1.
        var decisions = new Dictionary<int, string> { [1] = RowActions.FillBlanks };
        var committed = await imports.CommitAsync(upload.Batch.Id, Request(upload, decisions: decisions), None);
        Assert.Equal(2, committed.Batch.CreatedCount);
        Assert.Equal(1, committed.Batch.UpdatedCount);
        Assert.Equal(1, committed.Batch.SkippedCount);

        var merged = await app.Db.Patients.SingleAsync(p => p.Id == existing.Id);
        Assert.Equal("Latex", merged.Allergies);                                  // kept, not replaced by "Penicillin"
        Assert.Equal("ahmad@example.com", merged.Email);                          // was empty, now filled
        Assert.Equal("Abdoun", merged.Address);
        Assert.Equal("Ahmad Khalil", merged.FullName);
        Assert.Equal(4, await app.Db.Patients.CountAsync());

        // A decision that is not offered for the row is ignored rather than obeyed.
        var second = await imports.UploadAsync(ImportKind.Patients, "p2.csv", Csv("Name,Mobile\n,0795550011\n"), null, None);
        var forced = await imports.PreviewAsync(second.Batch.Id, Request(second, decisions: new() { [1] = RowActions.Create }), None);
        Assert.Equal(0, forced.Summary.WillCreate);
    }

    [Fact]
    public async Task Options_control_missing_phones_date_order_and_file_numbers()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var taken = await app.AddPatientAsync("Existing", "0795550020");
        taken.FileNumber = "1001";
        await app.Db.SaveChangesAsync();
        var imports = app.Get<ImportService>();

        const string csv = "File No,Name,Mobile,DOB\n1001,Clash Number,0795550021,03/04/1990\n2002,No Phone,,03/04/1990\n";
        var upload = await imports.UploadAsync(ImportKind.Patients, "p.csv", Csv(csv), "Sheet", None);
        var options = new ImportOptions { AllowMissingPhone = true, DateFormat = "mdy" };
        var preview = await imports.PreviewAsync(upload.Batch.Id, Request(upload, options), None);

        Assert.Equal(2, preview.Summary.WillCreate);
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 1).Warnings, w => w.Contains("already in use"));
        await imports.CommitAsync(upload.Batch.Id, Request(upload, options), None);

        var clash = await app.Db.Patients.SingleAsync(p => p.FullName == "Clash Number");
        Assert.NotEqual("1001", clash.FileNumber);                                // a new number was assigned
        Assert.Equal("1001", clash.ExternalId);                                   // the original stays as the reference
        Assert.Equal(new DateOnly(1990, 3, 4), clash.DateOfBirth);                // month first, as chosen
        var noPhone = await app.Db.Patients.SingleAsync(p => p.FullName == "No Phone");
        Assert.Equal("2002", noPhone.FileNumber);
        Assert.Equal("", noPhone.PhoneNormalized);
    }

    [Fact]
    public async Task Mapping_problems_are_reported_before_any_row_is_checked()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.Patients, "p.csv", Csv("A,B\nx,y\n"), null, None);

        var nothingMapped = await imports.PreviewAsync(upload.Batch.Id, new ImportRunRequest([], null, null), None);
        Assert.Contains(nothingMapped.MappingProblems, p => p.Contains("Full name"));
        Assert.Empty(nothingMapped.Rows);

        var wrongColumn = await imports.PreviewAsync(upload.Batch.Id, new ImportRunRequest(new() { [F.FullName] = "Missing" }, null, null), None);
        Assert.Contains(wrongColumn.MappingProblems, p => p.Contains("'Missing'"));
        await Assert.ThrowsAsync<RequestValidationException>(() => imports.CommitAsync(upload.Batch.Id, new ImportRunRequest([], null, null), None));
        Assert.Empty(await app.Db.Patients.ToListAsync());

        // An upload that was never imported can simply be discarded.
        await imports.DeleteAsync(upload.Batch.Id, None);
        Assert.Empty(await app.Db.ImportBatches.ToListAsync());
        Assert.Empty(await app.Db.ImportRows.ToListAsync());
    }

    [Fact]
    public async Task Unusable_files_are_refused_with_a_reason()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();

        await Assert.ThrowsAsync<RequestValidationException>(() => imports.UploadAsync(ImportKind.Patients, "p.csv", Csv(""), null, None));
        await Assert.ThrowsAsync<RequestValidationException>(() => imports.UploadAsync(ImportKind.Patients, "p.csv", Csv("Name,Phone\n"), null, None));
        await Assert.ThrowsAsync<RequestValidationException>(() => imports.UploadAsync(ImportKind.Patients, "p.docx", Csv("Name\nx\n"), null, None));
        var tooMany = "Name\n" + string.Concat(Enumerable.Range(0, ImportService.MaxRows + 1).Select(i => $"P{i}\n"));
        await Assert.ThrowsAsync<RequestValidationException>(() => imports.UploadAsync(ImportKind.Patients, "p.csv", Csv(tooMany), null, None));
        Assert.Empty(await app.Db.ImportBatches.ToListAsync());
    }

    [Fact]
    public async Task The_report_lists_every_row_with_its_result_before_and_after_import()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv), "Old System", None);
        await Assert.ThrowsAsync<BusinessRuleException>(() => imports.ReportAsync(upload.Batch.Id, false, None));   // nothing previewed yet

        await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);
        var (dryRun, name) = await imports.ReportAsync(upload.Batch.Id, false, None);
        var before = CsvText.Parse(Encoding.UTF8.GetString(dryRun));
        Assert.Equal("patients-import-report.csv", name);
        Assert.Equal(["Row", "Result", "Details", "File No", "Name"], before.Headers.Take(5));
        Assert.Equal(5, before.Rows.Count);
        Assert.Equal("Will be imported", before.Rows[0][1]);
        Assert.Equal("Error", before.Rows[2][1]);
        Assert.Equal("No Phone Person", before.Rows[2][4]);                       // the original cells, ready to correct

        var problems = CsvText.Parse(Encoding.UTF8.GetString((await imports.ReportAsync(upload.Batch.Id, true, None)).Content));
        Assert.Equal(3, problems.Rows.Count);                                     // two errors and one row with warnings

        await imports.CommitAsync(upload.Batch.Id, Request(upload), None);
        var after = CsvText.Parse(Encoding.UTF8.GetString((await imports.ReportAsync(upload.Batch.Id, false, None)).Content));
        Assert.Equal("Created", after.Rows[0][1]);
        Assert.Equal("Error", after.Rows[4][1]);
    }

    [Fact]
    public async Task Undo_removes_untouched_records_and_keeps_those_already_in_use()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv), "Old System", None);
        await imports.CommitAsync(upload.Batch.Id, Request(upload), None);

        // One imported patient is already being treated.
        var sara = await app.Db.Patients.SingleAsync(p => p.FullName == "Sara Haddad");
        app.Db.Appointments.Add(new Appointment { PatientId = sara.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddDays(1),
            EndTime = app.Clock.UtcNow.AddDays(1).AddMinutes(30), Treatment = "Check-up" });
        await app.Db.SaveChangesAsync();

        var undone = await imports.UndoAsync(upload.Batch.Id, None);
        Assert.Equal(2, undone.Removed);
        Assert.Equal(1, undone.Kept);
        Assert.Equal(ImportBatchStatus.Committed, undone.Batch.Status);           // still partly in effect
        Assert.Single(await app.Db.Patients.ToListAsync());

        // The corrected file can be imported again: the removed patients and their file numbers are free.
        var again = await imports.UploadAsync(ImportKind.Patients, "fixed.csv", Csv(PatientsCsv), "Old System", None);
        var preview = await imports.PreviewAsync(again.Batch.Id, Request(again), None);
        Assert.Equal(1, preview.Summary.AlreadyImported);
        Assert.Equal(2, preview.Summary.WillCreate);
        await imports.CommitAsync(again.Batch.Id, Request(again), None);
        Assert.Equal("1001", (await app.Db.Patients.SingleAsync(p => p.FullName == "Ahmad Khalil")).FileNumber);

        // Nothing in use: the whole import is undone.
        var clean = await imports.UndoAsync(again.Batch.Id, None);
        Assert.Equal(ImportBatchStatus.Undone, clean.Batch.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => imports.UndoAsync(again.Batch.Id, None));
    }

    [Fact]
    public async Task One_clinic_cannot_see_or_commit_another_clinics_import()
    {
        using var app = new TestApp();
        await app.AddClinicAsync("Clinic A");
        var upload = await app.Get<ImportService>().UploadAsync(ImportKind.Patients, "p.csv", Csv(PatientsCsv), null, None);

        await app.AddClinicAsync("Clinic B");
        using var scope = app.NewScope();
        var imports = scope.ServiceProvider.GetRequiredService<ImportService>();
        Assert.Empty(await imports.ListAsync(None));
        await Assert.ThrowsAsync<NotFoundException>(() => imports.GetAsync(upload.Batch.Id, None));
        await Assert.ThrowsAsync<NotFoundException>(() => imports.CommitAsync(upload.Batch.Id, Request(upload), None));
        await Assert.ThrowsAsync<NotFoundException>(() => imports.ReportAsync(upload.Batch.Id, false, None));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.ToListAsync());
    }

    // ------------------------------------------------------------------ appointments

    private static async Task<(TestApp App, TestClinic Clinic, Patient Ahmad)> ClinicWithPatientsAsync()
    {
        var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var ahmad = await app.AddPatientAsync("Ahmad Khalil", "0791234567");
        ahmad.FileNumber = "1001";
        var sara = await app.AddPatientAsync("Sara Haddad", "0795550002");
        sara.FileNumber = "1002";
        // A mother and child who share one phone number.
        await app.AddPatientAsync("Mona Saleh", "0795550009");
        await app.AddPatientAsync("Omar Saleh", "0795550009");
        await app.Db.SaveChangesAsync();
        return (app, clinic, ahmad);
    }

    [Fact]
    public async Task Appointments_attach_to_the_right_patient_and_no_attendance_is_invented()
    {
        var (app, clinic, ahmad) = await ClinicWithPatientsAsync();
        using var _ = app;
        var imports = app.Get<ImportService>();
        // "Today" is 7 October 2026 in the test clock.
        const string csv = """
            File No,Phone,Patient,Date,Time,Dentist,Reason,Status
            1001,,,01/09/2026,10:00,Dr Test,Filling,Completed
            1001,,,20/10/2026,16:30,Dr. Test,Crown prep,
            ,0795550009,Omar Saleh,21/10/2026,9:00 AM,د. Test,Check-up,Confirmed
            ,0795550009,,22/10/2026,09:00,Dr Test,Check-up,
            1002,,,02/09/2026,11:00,Dr Test,Cleaning,
            1002,,,03/09/2026,11:00,Dr Unknown,Cleaning,No-show
            9999,,,20/10/2026,10:00,Dr Test,Check-up,
            1002,,,25/10/2026,,Dr Test,Check-up,
            1002,,,26/10/2026,10:00,Dr Test,Check-up,Completed
            """;
        var upload = await imports.UploadAsync(ImportKind.Appointments, "appointments.csv", Csv(csv), "Old System", None);
        var preview = await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);

        Assert.Equal(3, preview.Summary.WillCreate);
        string Error(int row) => preview.Rows.Single(r => r.RowNumber == row).Errors.Single();
        Assert.Contains("Several patients share this phone number", Error(4));
        Assert.Contains("in the past and the file gives no status", Error(5));    // attendance is not assumed
        Assert.Contains("Dentist 'Dr Unknown' was not found", Error(6));
        Assert.Contains("No patient found for 9999", Error(7));
        Assert.Contains("The time is missing", Error(8));
        Assert.Contains("in the future cannot be completed", Error(9));

        await imports.CommitAsync(upload.Batch.Id, Request(upload), None);
        var appointments = await app.Db.Appointments.OrderBy(a => a.StartTime).ToListAsync();
        Assert.Equal(3, appointments.Count);

        var past = appointments[0];
        Assert.Equal(ahmad.Id, past.PatientId);
        Assert.Equal(AppointmentStatus.Completed, past.Status);
        Assert.Equal(new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc), past.StartTime);   // 10:00 in Amman
        Assert.Equal(clinic.DentistId, past.DentistId);
        Assert.Equal(upload.Batch.Id, past.ImportBatchId);
        Assert.Equal(past.StartTime, (await app.Db.Patients.SingleAsync(p => p.Id == ahmad.Id)).LastVisitAt);

        Assert.Equal(AppointmentStatus.Scheduled, appointments[1].Status);        // a future row without a status is simply booked
        Assert.Equal("Omar Saleh", (await app.Db.Patients.SingleAsync(p => p.Id == appointments[2].PatientId)).FullName);
        Assert.Equal(AppointmentStatus.Confirmed, appointments[2].Status);

        // Importing the same file again adds nothing.
        var again = await imports.UploadAsync(ImportKind.Appointments, "appointments.csv", Csv(csv), "Old System", None);
        Assert.Equal(0, (await imports.PreviewAsync(again.Batch.Id, Request(again), None)).Summary.WillCreate);
    }

    [Fact]
    public async Task Past_appointments_without_a_status_are_completed_only_by_explicit_choice()
    {
        var (app, clinic, ahmad) = await ClinicWithPatientsAsync();
        using var _ = app;
        var imports = app.Get<ImportService>();
        const string csv = "File No,Date,Time\n1001,01/09/2026,10:00\n1002,2026-09-02 11:30,\n";
        var upload = await imports.UploadAsync(ImportKind.Appointments, "a.csv", Csv(csv), null, None);

        var strict = await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);
        Assert.Empty(strict.MappingProblems);
        Assert.Equal(2, strict.Summary.Errors);                                    // no dentist, and no status for a past visit

        var options = new ImportOptions { AssumePastAppointmentsCompleted = true, DefaultDentistId = clinic.DentistId, DefaultDurationMinutes = 45 };
        await imports.CommitAsync(upload.Batch.Id, Request(upload, options), None);

        var appointments = await app.Db.Appointments.OrderBy(a => a.StartTime).ToListAsync();
        Assert.All(appointments, a => Assert.Equal(AppointmentStatus.Completed, a.Status));
        Assert.Equal(TimeSpan.FromMinutes(45), appointments[0].EndTime - appointments[0].StartTime);
        Assert.Equal(new DateTime(2026, 9, 2, 8, 30, 0, DateTimeKind.Utc), appointments[1].StartTime);   // time taken from the date cell

        // Undo removes them and takes the last-visit date back with them.
        var undone = await imports.UndoAsync(upload.Batch.Id, None);
        Assert.Equal(2, undone.Removed);
        Assert.Empty(await app.Db.Appointments.ToListAsync());
        Assert.Null((await app.Db.Patients.SingleAsync(p => p.Id == ahmad.Id)).LastVisitAt);
    }

    // ------------------------------------------------------------------ opening balances

    [Fact]
    public async Task Opening_balances_reconcile_to_the_file_and_cannot_be_counted_twice()
    {
        var (app, _, ahmad) = await ClinicWithPatientsAsync();
        using var __ = app;
        var imports = app.Get<ImportService>();
        const string csv = """
            File No,Balance,As of,Reference
            1001,"1,250.500",30/09/2026,INV-2091
            1002,75,30/09/2026,
            1002,0,30/09/2026,
            1001,-20,30/09/2026,
            1001,abc,30/09/2026,
            7777,40,30/09/2026,
            1002,10,30/12/2026,
            """;
        var upload = await imports.UploadAsync(ImportKind.OpeningBalances, "balances.csv", Csv(csv), "Old System", None);
        var preview = await imports.PreviewAsync(upload.Batch.Id, Request(upload), None);

        Assert.Equal(2, preview.Summary.WillCreate);
        Assert.Equal(1325.5m, preview.Summary.TotalAmount);                        // the figure to check against the old system
        Assert.Equal(4, preview.Summary.Errors);
        Assert.Contains("Negative amounts", preview.Rows.Single(r => r.RowNumber == 4).Errors[0]);
        Assert.Contains("could not be read", preview.Rows.Single(r => r.RowNumber == 5).Errors[0]);
        Assert.Contains("in the future", preview.Rows.Single(r => r.RowNumber == 7).Errors[0]);
        Assert.Equal(RowActions.Skip, preview.Rows.Single(r => r.RowNumber == 3).Action);   // zero balance: nothing to carry

        var committed = await imports.CommitAsync(upload.Batch.Id, Request(upload), None);
        Assert.Equal(1325.5m, committed.Batch.TotalAmount);

        var invoice = await app.Db.Invoices.Include(i => i.Items).SingleAsync(i => i.PatientId == ahmad.Id);
        Assert.True(invoice.IsOpeningBalance);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(1250.5m, invoice.Total);
        Assert.Equal(1250.5m, invoice.Balance);
        Assert.Equal(0m, invoice.AmountPaid);                                      // no payment history is made up
        Assert.Contains("Opening balance carried over from Old System", invoice.Notes);
        Assert.Contains("INV-2091", invoice.Notes);
        Assert.Single(invoice.Items);
        Assert.Empty(await app.Db.Payments.ToListAsync());
        Assert.Empty(await app.Db.TreatmentRecords.ToListAsync());

        // A second import of balances for the same patients is flagged, and skipped unless a person decides otherwise.
        var again = await imports.UploadAsync(ImportKind.OpeningBalances, "balances2.csv", Csv("File No,Balance\n1001,50\n"), "Old System", None);
        var second = await imports.PreviewAsync(again.Batch.Id, Request(again), None);
        Assert.Equal(RowVerdict.Duplicate, second.Rows.Single().Verdict);
        Assert.Equal(0, second.Summary.WillCreate);
        Assert.Contains(second.Rows.Single().Warnings, w => w.Contains("count the debt twice"));

        // Undo voids unpaid opening balances and keeps the one with a payment.
        var paid = await app.Db.Invoices.SingleAsync(i => i.PatientId != ahmad.Id);
        app.Db.Payments.Add(new Payment { ReceiptNumber = "RC-1", PatientId = paid.PatientId, InvoiceId = paid.Id, Amount = 25, Method = PaymentMethod.Cash, PaidAt = app.Clock.UtcNow });
        await app.Db.SaveChangesAsync();
        var undone = await imports.UndoAsync(upload.Batch.Id, None);
        Assert.Equal(1, undone.Removed);
        Assert.Equal(1, undone.Kept);
        Assert.Equal(InvoiceStatus.Void, (await app.Db.Invoices.SingleAsync(i => i.PatientId == ahmad.Id)).Status);
    }

    // ------------------------------------------------------------------ failure and concurrency (real PostgreSQL)

    private sealed class FailingSequences(ISequenceGenerator inner) : ISequenceGenerator
    {
        public static bool Fail;
        private int _calls;

        public Task<string> NextAsync(SequenceKind kind, CancellationToken ct) =>
            Fail && ++_calls >= 2 ? throw new InvalidOperationException("simulated crash half-way through the import") : inner.NextAsync(kind, ct);
    }

    /// <summary>A crash in the middle of an import must leave no half-imported data, and the import must be retryable.</summary>
    [PostgresFact]
    public async Task A_failed_import_writes_nothing_and_can_be_retried()
    {
        using var app = new TestApp(postgres: true, configure: services =>
        {
            services.AddScoped<SequenceGenerator>();
            services.AddScoped<ISequenceGenerator>(sp => new FailingSequences(sp.GetRequiredService<SequenceGenerator>()));
        });
        await app.AddClinicAsync();
        const string csv = "Name,Mobile\nOne,0795550101\nTwo,0795550102\nThree,0795550103\n";
        Guid batchId;
        ImportUploadDto upload;
        using (var scope = app.NewScope())
        {
            var imports = scope.ServiceProvider.GetRequiredService<ImportService>();
            upload = await imports.UploadAsync(ImportKind.Patients, "p.csv", Csv(csv), null, None);
            batchId = upload.Batch.Id;
            FailingSequences.Fail = true;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => imports.CommitAsync(batchId, Request(upload), None));
            }
            finally
            {
                FailingSequences.Fail = false;
            }
        }

        using (var scope = app.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Patients.ToListAsync());                         // nothing half-imported
            var batch = await db.ImportBatches.SingleAsync();
            Assert.Equal(ImportBatchStatus.Uploaded, batch.Status);
            Assert.Contains("nothing was imported", batch.LastError);
            Assert.All(await db.ImportRows.ToListAsync(), r => Assert.Equal(ImportRowStatus.Pending, r.Status));

            var committed = await scope.ServiceProvider.GetRequiredService<ImportService>().CommitAsync(batchId, Request(upload), None);
            Assert.Equal(3, committed.Batch.CreatedCount);
            Assert.Null(committed.Batch.LastError);
            Assert.Equal(3, await db.Patients.CountAsync());
        }
    }

    /// <summary>Two people pressing "Import" on the same batch at once: exactly one import happens.</summary>
    [PostgresFact]
    public async Task Simultaneous_commits_of_one_batch_import_it_once()
    {
        using var app = new TestApp(postgres: true);
        await app.AddClinicAsync();
        var upload = await app.Get<ImportService>().UploadAsync(ImportKind.Patients, "patients.csv", Csv(PatientsCsv), "Old System", None);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var scope = app.NewScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ImportService>().CommitAsync(upload.Batch.Id, Request(upload), None);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(1, results.Count(ok => ok));
        using var check = app.NewScope();
        Assert.Equal(3, await check.ServiceProvider.GetRequiredService<AppDbContext>().Patients.CountAsync());
    }
}
