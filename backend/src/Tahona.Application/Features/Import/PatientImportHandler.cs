using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using static Tahona.Application.Features.Import.ImportDefinitions;

namespace Tahona.Application.Features.Import;

/// <summary>
/// Imports patients. A row that matches an existing patient is never merged silently: it is skipped, or — only when
/// the person chooses it — used to fill fields that are empty on the existing record. Existing values are never overwritten.
/// </summary>
public class PatientImportHandler(IAppDbContext db, ISequenceGenerator sequences, IClock clock, IClinicTime clinicTime) : IImportHandler
{
    public ImportKind Kind => ImportKind.Patients;

    private sealed record Parsed(
        string FullName, string? FullNameAr, string Phone, string PhoneNormalized, string? WhatsApp, string? Email, DateOnly? DateOfBirth,
        Gender? Gender, string? NationalId, string? Address, string? City, string? FileNumber, string? ExternalId, string? Allergies,
        string? MedicalConditions, string? Medications, string? MedicalNotes, string? InsuranceProvider, string? InsurancePolicyNumber,
        DateTime? RegisteredAt, DateTime? LastVisitAt, string? Language, MessagingConsent? Consent)
    {
        /// <summary>Whether the source file number can be used as the TahonaOS file number.</summary>
        public bool UseFileNumber { get; init; }
    }

    private sealed record Existing(Guid Id, string FileNumber, string FullName, string? FullNameAr, string Phone, string? NationalId,
        DateOnly? DateOfBirth, string? ExternalId, string? SourceSystem);

    public async Task<IReadOnlyList<RowEvaluation>> EvaluateAsync(ImportInput input, CancellationToken ct)
    {
        var existing = await db.Patients.AsNoTracking()
            .Select(p => new Existing(p.Id, p.FileNumber, p.FullName, p.FullNameAr, p.PhoneNormalized, p.NationalId, p.DateOfBirth, p.ExternalId, p.SourceSystem))
            .ToListAsync(ct);
        var byExternal = existing.Where(p => p.ExternalId is not null && p.SourceSystem == input.SourceSystem).ToLookup(p => p.ExternalId!.ToUpperInvariant());
        var byNationalId = existing.Where(p => !string.IsNullOrEmpty(p.NationalId)).ToLookup(p => p.NationalId!.ToUpperInvariant());
        var byPhone = existing.Where(p => p.Phone.Length > 0).ToLookup(p => p.Phone);
        var takenFileNumbers = existing.Select(p => p.FileNumber.ToUpperInvariant()).ToHashSet();

        var today = clinicTime.Today;
        var options = input.Options;
        var seenExternal = new Dictionary<string, int>();
        var seenIdentity = new Dictionary<string, int>();
        var results = new List<RowEvaluation>();

        foreach (var row in input.Rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            var name = row[F.FullName];
            if (name.Length == 0) name = $"{row[F.FirstName]} {row[F.LastName]}".Trim();
            var phoneRaw = row[F.Phone];
            var phone = PhoneNumber.Normalize(phoneRaw);
            var display = string.Join(" · ", new[] { name, phoneRaw }.Where(v => v.Length > 0));
            if (display.Length == 0) display = $"Row {row.Number}";

            if (ImportRules.IsTemplateExample(name, row[F.FileNumber]))
            {
                results.Add(ImportRules.Error(row.Number, display, [ImportRules.TemplateExampleMessage]));
                continue;
            }

            if (name.Length == 0) errors.Add("The name is missing.");
            else if (name.Length > 150) errors.Add("The name is longer than 150 characters.");

            if (phoneRaw.Length == 0)
            {
                if (options.AllowMissingPhone) warnings.Add("No phone number: the patient cannot be messaged or found by phone.");
                else errors.Add("The phone number is missing. Add it, or choose to import patients without a phone.");
            }
            else if (phone.Length is < 7 or > 15) errors.Add($"'{phoneRaw}' is not a valid phone number.");

            // Optional values that cannot be read are left empty and reported, never guessed.
            var dob = OptionalDate(row[F.DateOfBirth], "date of birth", options, warnings);
            if (dob > today) { warnings.Add($"The date of birth {row[F.DateOfBirth]} is in the future and was left empty."); dob = null; }
            var registered = OptionalDate(row[F.RegisteredAt], "registration date", options, warnings);
            if (registered > today) { warnings.Add("The registration date is in the future; today is used instead."); registered = null; }
            var lastVisit = OptionalDate(row[F.LastVisitAt], "last visit date", options, warnings);
            if (lastVisit > today) { warnings.Add("The last visit date is in the future and was left empty."); lastVisit = null; }

            Gender? gender = null;
            if (row.Has(F.Gender) && (gender = ImportParsing.GenderOf(row[F.Gender])) is null)
                warnings.Add($"Gender '{row[F.Gender]}' was not recognised and was left empty.");
            string? language = null;
            if (row.Has(F.Language) && (language = ImportParsing.Language(row[F.Language])) is null)
                warnings.Add($"Language '{row[F.Language]}' was not recognised; the clinic default is used.");
            MessagingConsent? consent = null;
            if (row.Has(F.Consent))
            {
                var agreed = ImportParsing.YesNo(row[F.Consent]);
                if (agreed is null) warnings.Add($"Messaging consent '{row[F.Consent]}' was not recognised and is recorded as not known.");
                else consent = agreed.Value ? MessagingConsent.Granted : MessagingConsent.OptedOut;
            }

            var email = row[F.Email].ToLowerInvariant();
            if (email.Length > 0 && (!ImportParsing.LooksLikeEmail(email) || email.Length > 200))
            {
                warnings.Add($"E-mail '{row[F.Email]}' is not valid and was left empty.");
                email = "";
            }
            var whatsApp = row[F.WhatsApp];
            if (whatsApp.Length > 0 && PhoneNumber.Normalize(whatsApp).Length is < 7 or > 15)
            {
                warnings.Add($"WhatsApp number '{whatsApp}' is not valid and was left empty.");
                whatsApp = "";
            }

            var fileNumber = row[F.FileNumber];
            var externalId = row.Has(F.ExternalId) ? row[F.ExternalId] : fileNumber;
            if (externalId.Length > 100) { errors.Add("The ID from the previous system is longer than 100 characters."); externalId = ""; }

            // Whether the source file number can be kept. The note is added only if the row ends up being created.
            var useFileNumber = false;
            string? fileNumberNote = null;
            if (options.KeepFileNumbers && fileNumber.Length > 0)
            {
                if (fileNumber.Length > 20) fileNumberNote = $"File number '{fileNumber}' is longer than 20 characters; a new number is assigned and the original is kept as a reference.";
                else if (takenFileNumbers.Contains(fileNumber.ToUpperInvariant())) fileNumberNote = $"File number '{fileNumber}' is already in use; a new number is assigned and the original is kept as a reference.";
                else useFileNumber = true;
            }

            if (errors.Count > 0)
            {
                results.Add(ImportRules.Error(row.Number, display, errors, warnings));
                continue;
            }

            // A value longer than its field is shortened, and the row says so.
            string? Fit(string field, int max)
            {
                var value = row[field];
                if (value.Length <= max) return value.Length == 0 ? null : value;
                warnings.Add($"'{Fields[field]}' is longer than {max} characters and was shortened.");
                return value[..max];
            }

            var parsed = new Parsed(name, Fit(F.FullNameAr, 150), phoneRaw, phone, whatsApp.Length == 0 ? null : whatsApp, email.Length == 0 ? null : email,
                dob, gender, Fit(F.NationalId, 40), Fit(F.Address, 300), Fit(F.City, 80), fileNumber.Length == 0 ? null : fileNumber,
                externalId.Length == 0 ? null : externalId, Fit(F.Allergies, 1000), Fit(F.MedicalConditions, 1000), Fit(F.Medications, 1000),
                Fit(F.MedicalNotes, 4000), Fit(F.InsuranceProvider, 120), Fit(F.InsurancePolicyNumber, 60),
                registered is null ? null : clinicTime.ToUtc(registered.Value.ToDateTime(new TimeOnly(12, 0))),
                lastVisit is null ? null : clinicTime.ToUtc(lastVisit.Value.ToDateTime(new TimeOnly(12, 0))), language, consent)
            { UseFileNumber = useFileNumber };

            // ---------------------------------------------------------------- duplicates, strongest evidence first
            var externalKey = externalId.ToUpperInvariant();
            var identityKey = $"{phone}|{ImportParsing.Key(name)}";

            if (externalKey.Length > 0 && byExternal[externalKey].FirstOrDefault() is { } imported)
            {
                results.Add(new RowEvaluation(row.Number, RowVerdict.AlreadyImported, RowActions.Skip, [RowActions.Skip], [],
                    [.. warnings, "This patient was imported from the same source before."], display, imported.Id, Label(imported)));
                continue;
            }
            if (externalKey.Length > 0 && seenExternal.TryGetValue(externalKey, out var firstRow)
                || phone.Length > 0 && seenIdentity.TryGetValue(identityKey, out firstRow))
            {
                var allowed = new[] { RowActions.Skip, RowActions.Create };
                results.Add(new RowEvaluation(row.Number, RowVerdict.Duplicate, ImportRules.Decide(input, row.Number, RowActions.Skip, allowed), allowed, [],
                    [.. warnings, $"Same patient as row {firstRow} of this file."], display, null, $"Row {firstRow}") { Model = parsed });
                continue;
            }
            if (externalKey.Length > 0) seenExternal[externalKey] = row.Number;
            if (phone.Length > 0) seenIdentity[identityKey] = row.Number;
            if (useFileNumber) takenFileNumbers.Add(fileNumber.ToUpperInvariant());
            if (fileNumberNote is not null) warnings.Add(fileNumberNote);

            var samePhone = phone.Length > 0 ? byPhone[phone].ToList() : [];
            var exact = (parsed.NationalId is not null ? byNationalId[parsed.NationalId.ToUpperInvariant()].FirstOrDefault() : null)
                        ?? samePhone.FirstOrDefault(p => ImportParsing.SameName(p.FullName, name) || ImportParsing.SameName(p.FullNameAr, name)
                                                         || (parsed.FullNameAr is not null && ImportParsing.SameName(p.FullNameAr, parsed.FullNameAr)));
            if (exact is not null)
            {
                var allowed = new[] { RowActions.Skip, RowActions.FillBlanks, RowActions.Create };
                results.Add(new RowEvaluation(row.Number, RowVerdict.Duplicate, ImportRules.Decide(input, row.Number, options.DuplicateAction, allowed), allowed,
                    [], [.. warnings, "A patient with the same name and phone number (or national ID) already exists."], display, exact.Id, Label(exact))
                    { Model = parsed });
                continue;
            }

            var possible = samePhone.FirstOrDefault()
                           ?? (dob is null ? null : existing.FirstOrDefault(p => p.DateOfBirth == dob && ImportParsing.SameName(p.FullName, name)));
            if (possible is not null)
            {
                var allowed = new[] { RowActions.Create, RowActions.Skip, RowActions.FillBlanks };
                var reason = samePhone.Count > 0
                    ? "An existing patient has the same phone number under a different name (often a family member)."
                    : "An existing patient has the same name and date of birth but a different phone number.";
                results.Add(new RowEvaluation(row.Number, RowVerdict.PossibleDuplicate,
                    ImportRules.Decide(input, row.Number, options.PossibleDuplicateAction, allowed), allowed, [], [.. warnings, reason], display,
                    possible.Id, Label(possible)) { Model = parsed });
                continue;
            }

            results.Add(new RowEvaluation(row.Number, RowVerdict.Ready, RowActions.Create, [RowActions.Create, RowActions.Skip], [], warnings, display, null, null)
                { Model = parsed });
        }
        return results;
    }

    public async Task<ApplyResult> ApplyAsync(ImportBatch batch, IReadOnlyList<RowEvaluation> rows, ImportInput input, CancellationToken ct)
    {
        var outcome = new Dictionary<int, (ImportRowStatus, string, Guid)>();
        var now = clock.UtcNow;
        var defaultLanguage = await db.Clinics.AsNoTracking().Select(c => c.DefaultLanguage).FirstAsync(ct);

        // File numbers in use, including the ones this import keeps from the source: a generated number must avoid them all.
        var taken = (await db.Patients.IgnoreQueryFilters().Where(x => x.ClinicId == batch.ClinicId).Select(x => x.FileNumber).ToListAsync(ct))
            .Select(n => n.ToUpperInvariant()).ToHashSet();
        taken.UnionWith(rows.Where(r => r.Action == RowActions.Create && r.Model is Parsed { UseFileNumber: true })
            .Select(r => ((Parsed)r.Model!).FileNumber!.ToUpperInvariant()));

        async Task<string> NextFileNumberAsync()
        {
            while (true)
            {
                var number = await sequences.NextAsync(SequenceKind.Patient, ct);
                if (taken.Add(number.ToUpperInvariant())) return number;
            }
        }

        var toFill = rows.Where(r => r.Action == RowActions.FillBlanks && r.MatchId is not null).ToList();
        var fillIds = toFill.Select(r => r.MatchId!.Value).Distinct().ToList();
        var targets = await db.Patients.Where(p => fillIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var row in rows)
        {
            if (row.Model is not Parsed p) continue;
            if (row.Action == RowActions.Create)
            {
                var patient = new Patient
                {
                    FileNumber = p.UseFileNumber ? p.FileNumber! : await NextFileNumberAsync(),
                    FullName = p.FullName, FullNameAr = p.FullNameAr, Phone = p.Phone, PhoneNormalized = p.PhoneNormalized, WhatsAppNumber = p.WhatsApp,
                    Email = p.Email, DateOfBirth = p.DateOfBirth, Gender = p.Gender, NationalId = p.NationalId, Address = p.Address, City = p.City,
                    Allergies = p.Allergies, MedicalConditions = p.MedicalConditions, CurrentMedications = p.Medications, MedicalNotes = p.MedicalNotes,
                    InsuranceProvider = p.InsuranceProvider, InsurancePolicyNumber = p.InsurancePolicyNumber,
                    PreferredLanguage = p.Language ?? defaultLanguage, RegisteredAt = p.RegisteredAt ?? now, LastVisitAt = p.LastVisitAt,
                    ExternalId = p.ExternalId, SourceSystem = input.SourceSystem, ImportBatchId = batch.Id
                };
                if (p.Consent is not null)
                {
                    patient.MessagingConsent = p.Consent.Value;
                    patient.MessagingConsentAt = now;
                    patient.MessagingConsentSource = $"Import: {input.SourceSystem}";
                }
                db.Patients.Add(patient);
                outcome[row.RowNumber] = (ImportRowStatus.Created, nameof(Patient), patient.Id);
            }
            else if (row.Action == RowActions.FillBlanks && row.MatchId is { } id && targets.TryGetValue(id, out var target))
            {
                if (FillBlanks(target, p, input.SourceSystem, now)) outcome[row.RowNumber] = (ImportRowStatus.Updated, nameof(Patient), target.Id);
            }
        }
        return new ApplyResult(outcome, null);
    }

    /// <summary>Copies values only into fields that are empty on the existing patient. Returns whether anything changed.</summary>
    private static bool FillBlanks(Patient target, Parsed p, string sourceSystem, DateTime now)
    {
        var changed = false;
        void Fill(string? current, string? incoming, Action<string> set)
        {
            if (!string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(incoming)) return;
            set(incoming);
            changed = true;
        }

        Fill(target.FullNameAr, p.FullNameAr, v => target.FullNameAr = v);
        Fill(target.WhatsAppNumber, p.WhatsApp, v => target.WhatsAppNumber = v);
        Fill(target.Email, p.Email, v => target.Email = v);
        Fill(target.NationalId, p.NationalId, v => target.NationalId = v);
        Fill(target.Address, p.Address, v => target.Address = v);
        Fill(target.City, p.City, v => target.City = v);
        Fill(target.Allergies, p.Allergies, v => target.Allergies = v);
        Fill(target.MedicalConditions, p.MedicalConditions, v => target.MedicalConditions = v);
        Fill(target.CurrentMedications, p.Medications, v => target.CurrentMedications = v);
        Fill(target.MedicalNotes, p.MedicalNotes, v => target.MedicalNotes = v);
        Fill(target.InsuranceProvider, p.InsuranceProvider, v => target.InsuranceProvider = v);
        Fill(target.InsurancePolicyNumber, p.InsurancePolicyNumber, v => target.InsurancePolicyNumber = v);
        Fill(target.Phone, p.Phone, v => { target.Phone = v; target.PhoneNormalized = p.PhoneNormalized; });
        if (target.ExternalId is null && p.ExternalId is not null) { target.ExternalId = p.ExternalId; target.SourceSystem ??= sourceSystem; changed = true; }
        if (target.DateOfBirth is null && p.DateOfBirth is not null) { target.DateOfBirth = p.DateOfBirth; changed = true; }
        if (target.Gender is null && p.Gender is not null) { target.Gender = p.Gender; changed = true; }
        if (target.LastVisitAt is null && p.LastVisitAt is not null) { target.LastVisitAt = p.LastVisitAt; changed = true; }
        if (target.MessagingConsent == MessagingConsent.Unknown && p.Consent is not null)
        {
            target.MessagingConsent = p.Consent.Value;
            target.MessagingConsentAt = now;
            target.MessagingConsentSource = $"Import: {sourceSystem}";
            changed = true;
        }
        return changed;
    }

    private static DateOnly? OptionalDate(string value, string what, ImportOptions options, List<string> warnings)
    {
        if (value.Length == 0) return null;
        var date = ImportParsing.Date(value, options.DateFormat);
        if (date is null) warnings.Add($"The {what} '{value}' could not be read and was left empty.");
        return date;
    }

    private static readonly Dictionary<string, string> Fields = ImportDefinitions.Patients.Fields.ToDictionary(f => f.Key, f => f.Label);

    private static string Label(Existing p) => $"{p.FullName} · {p.FileNumber}";
}
