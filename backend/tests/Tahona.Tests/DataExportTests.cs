using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Application.Features.Documents;
using Tahona.Application.Features.Export;
using Tahona.Application.Features.Import;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>A clinic can take its own records out, complete and usable, and nobody else's.</summary>
public class DataExportTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static async Task<Dictionary<string, byte[]>> UnzipAsync(DataExport export)
    {
        await using var content = export.Content;
        using var zip = new ZipArchive(content, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            using var buffer = new MemoryStream();
            await entry.Open().CopyToAsync(buffer);
            files[entry.FullName] = buffer.ToArray();
        }
        return files;
    }

    private static TabularFile Sheet(Dictionary<string, byte[]> files, string name) => CsvText.Parse(Encoding.UTF8.GetString(files[name]));

    [Fact]
    public async Task The_export_holds_the_clinics_records_with_their_relationships_and_no_secrets()
    {
        using var app = new TestApp();
        var other = await app.AddClinicAsync("Other Clinic");
        var stranger = await app.AddPatientAsync("Someone Else", "0795559999");
        app.Db.Add(new Payment { ReceiptNumber = "RC-X", PatientId = stranger.Id, Amount = 5m, Method = PaymentMethod.Cash, PaidAt = app.Clock.UtcNow,
            IsDeleted = true, DeletedAt = app.Clock.UtcNow, CancelReason = "Other clinic mistake" });
        await app.Db.SaveChangesAsync();

        var clinic = await app.AddClinicAsync("Export Clinic");
        var patient = await app.AddPatientAsync("أحمد خليل", "0791234567");
        patient.Allergies = "=Penicillin";                       // a value a spreadsheet could mistake for a formula
        var appointment = new Appointment { PatientId = patient.Id, DentistId = clinic.DentistId, StartTime = new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 9, 1, 7, 30, 0, DateTimeKind.Utc), Treatment = "Filling", Status = AppointmentStatus.Completed };
        var invoice = new Invoice { InvoiceNumber = "INV-1", PatientId = patient.Id, IssueDate = app.Clock.UtcNow, DueDate = app.Clock.UtcNow, Total = 120.5m,
            AmountPaid = 20.5m, Balance = 100m, Status = InvoiceStatus.PartiallyPaid };
        var payment = new Payment { ReceiptNumber = "RC-1", PatientId = patient.Id, Invoice = invoice, Amount = 30.5m, Method = PaymentMethod.CliQ, PaidAt = app.Clock.UtcNow };
        var refund = new Payment { ReceiptNumber = "RC-2", PatientId = patient.Id, Invoice = invoice, Amount = 10m, Type = PaymentType.Refund, Method = PaymentMethod.Cash,
            PaidAt = app.Clock.UtcNow, RefundOfPaymentId = payment.Id };
        var mistake = new Payment { ReceiptNumber = "RC-3", PatientId = patient.Id, Invoice = invoice, Amount = 99m, Method = PaymentMethod.Cash, PaidAt = app.Clock.UtcNow,
            IsDeleted = true, DeletedAt = app.Clock.UtcNow, CancelReason = "Entered twice", CancelledById = clinic.AdminId };
        app.Db.AddRange(appointment, invoice, payment, refund, mistake);
        await app.Db.SaveChangesAsync();
        await app.Get<DocumentService>().UploadAsync(new UploadDocumentCommand(patient.Id, DocumentCategory.XRay, "x ray.png", "image/png", Png.Length,
            new MemoryStream(Png), null, 16), CancellationToken.None);

        var export = await app.Get<DataExportService>().ExportAsync(includeDocuments: true, CancellationToken.None);
        Assert.EndsWith(".zip", export.FileName);
        var files = await UnzipAsync(export);

        // Every sheet is listed in the manifest with its real row count.
        var manifest = JsonDocument.Parse(files["manifest.json"]).RootElement;
        Assert.Equal("tahona-export", manifest.GetProperty("format").GetString());
        Assert.Equal("Export Clinic", manifest.GetProperty("clinic").GetProperty("Name").GetString());
        foreach (var file in manifest.GetProperty("files").EnumerateArray())
        {
            var sheet = Sheet(files, file.GetProperty("file").GetString()!);
            Assert.Equal(file.GetProperty("rows").GetInt32(), sheet.Rows.Count);
            Assert.Equal(file.GetProperty("columns").EnumerateArray().Select(c => c.GetString()), sheet.Headers);
        }
        Assert.Contains("README.txt", files.Keys);

        // Only this clinic's patient, readable in Arabic, with identifiers intact.
        var patients = Sheet(files, "patients.csv");
        var row = Assert.Single(patients.Rows);
        Assert.Equal(patient.Id.ToString(), row[0]);
        Assert.Equal("أحمد خليل", row[patients.Headers.ToList().IndexOf("full_name")]);
        Assert.Equal("'=Penicillin", row[patients.Headers.ToList().IndexOf("allergies")]);     // neutralised for spreadsheets
        Assert.Equal("=Penicillin", ImportParsing.Normalize(row[patients.Headers.ToList().IndexOf("allergies")]));   // and read back exactly
        Assert.DoesNotContain("Someone Else", Encoding.UTF8.GetString(files["patients.csv"]));

        // Relationships survive: the appointment and the payments point at the exported patient and invoice.
        var appointments = Sheet(files, "appointments.csv");
        Assert.Equal(patient.Id.ToString(), appointments.Rows[0][1]);
        Assert.Equal("2026-09-01T07:00:00Z", appointments.Rows[0][3]);
        Assert.Equal("Completed", appointments.Rows[0][6]);

        var payments = Sheet(files, "payments.csv");
        var paymentColumn = (string name) => payments.Headers.ToList().IndexOf(name);
        Assert.Equal(3, payments.Rows.Count);
        var refundRow = payments.Rows.Single(r => r[4] == "Refund");
        Assert.Equal("10.000", refundRow[5]);                                                   // refunds are positive amounts with their own type
        Assert.Equal(payment.Id.ToString(), refundRow[paymentColumn("refund_of_payment_id")]);
        Assert.Equal(invoice.Id.ToString(), refundRow[3]);
        Assert.Equal("", refundRow[paymentColumn("cancelled_at")]);
        // An entry cancelled as a mistake is still the clinic's record: exported, and marked so it is not counted.
        var cancelledRow = payments.Rows.Single(r => r[1] == "RC-3");
        Assert.NotEqual("", cancelledRow[paymentColumn("cancelled_at")]);
        Assert.Equal(clinic.AdminId.ToString(), cancelledRow[paymentColumn("cancelled_by_id")]);
        Assert.Equal("Entered twice", cancelledRow[paymentColumn("cancel_reason")]);
        Assert.DoesNotContain("Other clinic mistake", Encoding.UTF8.GetString(files["payments.csv"]));
        var invoices = Sheet(files, "invoices.csv");
        Assert.Equal("100.000", invoices.Rows[0][invoices.Headers.ToList().IndexOf("balance")]);

        // The uploaded file is in the archive at the path the documents sheet gives.
        var documents = Sheet(files, "documents.csv");
        var path = documents.Rows[0][^1];
        Assert.StartsWith("documents/", path);
        Assert.Equal(Png, files[path]);

        // No secrets, and no other clinic.
        var everything = string.Join("\n", files.Where(f => f.Key.EndsWith(".csv") || f.Key.EndsWith(".json")).Select(f => Encoding.UTF8.GetString(f.Value)));
        var staff = Sheet(files, "staff.csv");
        Assert.DoesNotContain(staff.Headers, h => h.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Other Clinic", everything);
        Assert.DoesNotContain(other.Id.ToString(), everything);

        // The export itself is on record.
        var audit = await app.Db.AuditLogs.SingleAsync(a => a.Action == "DataExported");
        Assert.Equal(clinic.AdminId, audit.UserId);
        Assert.Contains("1 files", audit.Summary);
    }

    [Fact]
    public async Task Files_are_left_out_unless_requested_and_an_empty_clinic_still_exports()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var files = await UnzipAsync(await app.Get<DataExportService>().ExportAsync(includeDocuments: false, CancellationToken.None));

        Assert.DoesNotContain(files.Keys, k => k.StartsWith("documents/"));
        Assert.Empty(Sheet(files, "clinic.csv").Rows.Skip(1));
        Assert.Single(Sheet(files, "staff.csv").Rows);
        var manifest = JsonDocument.Parse(files["manifest.json"]).RootElement;
        Assert.Contains(manifest.GetProperty("exclusions").EnumerateArray(), e => e.GetString()!.Contains("were not requested"));
    }
}
