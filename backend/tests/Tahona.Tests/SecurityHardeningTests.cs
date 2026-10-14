using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Application.Features.Ai;
using Tahona.Application.Features.Clinical;
using Tahona.Application.Features.Documents;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Ai;
using Tahona.Infrastructure.Messaging;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Services;

namespace Tahona.Tests;

/// <summary>Upload inspection, webhook verification, AI data minimisation and cross-record references.</summary>
public class SecurityHardeningTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

    // ------------------------------------------------------------------ uploads

    [Fact]
    public void File_signature_recognises_each_supported_type()
    {
        Assert.True(FileSignature.Matches(".png", Png, Png.Length));
        Assert.True(FileSignature.Matches(".jpg", [0xFF, 0xD8, 0xFF, 0xE0], 4));
        Assert.True(FileSignature.Matches(".pdf", "%PDF-1.7"u8, 8));
        Assert.True(FileSignature.Matches(".webp", [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8], 12));

        var dicom = new byte[132];
        "DICM"u8.CopyTo(dicom.AsSpan(128));
        Assert.True(FileSignature.Matches(".dcm", dicom, dicom.Length));

        var stl = new byte[84 + 50 * 2];
        BitConverter.TryWriteBytes(stl.AsSpan(80), 2u);
        Assert.True(FileSignature.Matches(".stl", stl.AsSpan(0, 132), stl.Length));
        Assert.True(FileSignature.Matches(".stl", "solid tooth"u8, 11));
    }

    [Fact]
    public void File_signature_rejects_content_that_does_not_match_the_extension()
    {
        Assert.False(FileSignature.Matches(".png", Html, Html.Length));
        Assert.False(FileSignature.Matches(".pdf", Png, Png.Length));
        Assert.False(FileSignature.Matches(".dcm", Png, Png.Length));
        Assert.False(FileSignature.Matches(".exe", Png, Png.Length));
    }

    [Fact]
    public async Task Upload_ignores_the_browser_content_type_and_inspects_the_bytes()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var documents = app.Get<DocumentService>();

        // An HTML page labelled as an image by the browser is refused, and nothing is stored.
        await Assert.ThrowsAsync<RequestValidationException>(() => documents.UploadAsync(
            new UploadDocumentCommand(patient.Id, DocumentCategory.XRay, "xray.png", "image/png", Html.Length, new MemoryStream(Html), null, null),
            CancellationToken.None));
        Assert.Empty(app.Files.Files);

        // A real PNG sent with a misleading content type is stored with the type the server decided.
        var saved = await documents.UploadAsync(
            new UploadDocumentCommand(patient.Id, DocumentCategory.XRay, "xray.png", "text/html", Png.Length, new MemoryStream(Png), null, null),
            CancellationToken.None);
        Assert.Equal("image/png", saved.ContentType);
        Assert.Equal(Png, Assert.Single(app.Files.Files).Value);
    }

    [Fact]
    public async Task Local_storage_refuses_paths_outside_its_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "tahona-test-" + Guid.NewGuid().ToString("N"), "storage");
        var storage = new LocalFileStorage(Options.Create(new StorageOptions { Root = root }));
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync("../storage2/secret.txt", CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync("../../etc/passwd", CancellationToken.None));

            var path = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "a.png", new MemoryStream(Png), CancellationToken.None);
            await using var read = await storage.OpenReadAsync(path, CancellationToken.None);
            Assert.NotNull(read);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(root)!, true);
        }
    }

    // ------------------------------------------------------------------ webhook

    private const string Secret = "app-secret";

    private static string Sign(byte[] body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body)).ToLowerInvariant();

    [Fact]
    public void Webhook_signature_is_checked_over_the_raw_body()
    {
        var body = Encoding.UTF8.GetBytes("""{"entry":[],"note":"مرحبا"}""");
        Assert.True(WhatsAppWebhook.IsSignatureValid(body, Sign(body), Secret));
        Assert.True(WhatsAppWebhook.IsSignatureValid(body, Sign(body).ToUpperInvariant().Replace("SHA256=", "sha256="), Secret));

        Assert.False(WhatsAppWebhook.IsSignatureValid(body, Sign(Encoding.UTF8.GetBytes("tampered")), Secret));
        Assert.False(WhatsAppWebhook.IsSignatureValid(body, Sign(body), "another-secret"));
        Assert.False(WhatsAppWebhook.IsSignatureValid(body, null, Secret));
        Assert.False(WhatsAppWebhook.IsSignatureValid(body, "", Secret));
    }

    [Fact]
    public void Webhook_payload_yields_statuses_and_inbound_messages_and_skips_the_rest()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[
              {"field":"messages","value":{"statuses":[
                 {"id":"wamid.S1","status":"delivered","timestamp":"1791360000"},
                 {"id":"wamid.S2","status":"failed","errors":[{"code":131026,"title":"Message undeliverable"}]},
                 {"id":"wamid.S3","status":"something_new"}]}},
              {"field":"messages","value":{"messages":[
                 {"id":"wamid.M1","from":"962791234567","timestamp":"1791360000","type":"text","text":{"body":"نعم، شكراً"}},
                 {"id":"wamid.M2","from":"962791234567","timestamp":"1791360001","type":"image","image":{"id":"x"}},
                 {"from":"962791234567","type":"text","text":{"body":"no id"}}]}},
              {"field":"account_update","value":"ignored"}]}]}
            """);

        var events = WhatsAppWebhook.Parse(body);

        var statuses = events.OfType<WhatsAppWebhook.StatusEvent>().ToList();
        Assert.Equal(2, statuses.Count);
        Assert.Equal(MessageStatus.Delivered, statuses[0].Status);
        Assert.Equal("Message undeliverable", statuses[1].Error);

        var inbound = events.OfType<WhatsAppWebhook.InboundEvent>().ToList();
        Assert.Equal(2, inbound.Count);
        Assert.Equal("نعم، شكراً", inbound[0].Text);
        Assert.Equal("[image]", inbound[1].Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791360000).UtcDateTime, inbound[0].ReceivedAt);
    }

    // ------------------------------------------------------------------ AI data minimisation

    private static async Task<(TestApp App, Patient Patient)> ClinicWithDebtorAsync(AiPatientDataMode mode)
    {
        var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara Haddad", "0791234567");
        app.Db.Invoices.Add(new Invoice
        {
            InvoiceNumber = "INV-1", PatientId = patient.Id, DentistId = clinic.DentistId, IssueDate = app.Clock.UtcNow.AddDays(-30),
            DueDate = app.Clock.UtcNow.AddDays(-16), Status = InvoiceStatus.Issued, Total = 120, Balance = 120
        });
        await app.Db.SaveChangesAsync();
        app.Ai.IsConfigured = true;
        app.AiPolicy.PatientData = mode;
        app.Ai.ToolsToCall.AddRange(["list_outstanding_balances", "find_patient", "get_revenue_summary"]);
        return (app, patient);
    }

    [Fact]
    public async Task By_default_the_ai_provider_receives_file_numbers_not_names_or_phone_numbers()
    {
        var (app, patient) = await ClinicWithDebtorAsync(AiPatientDataMode.Minimized);
        using var _ = app;

        var answer = await app.Get<AssistantService>().AskAsync(new AssistantRequest("Who owes us money?", null), CancellationToken.None);

        var sentToProvider = string.Join("\n", app.Ai.ToolResults);
        Assert.Contains(patient.FileNumber, sentToProvider);
        Assert.DoesNotContain("Sara", sentToProvider);
        Assert.DoesNotContain("Haddad", sentToProvider);
        Assert.DoesNotContain("791234567", sentToProvider);
        Assert.Contains("file number", app.Ai.LastSystemPrompt);

        // Staff still see who it is: the evidence table is built by the server, not by the model.
        var table = answer.Data.First(t => t.Title == "Outstanding balances");
        Assert.Contains("Sara Haddad", table.Rows[0][0]);
        Assert.Contains(patient.FileNumber, table.Rows[0][0]);
    }

    [Fact]
    public async Task With_patient_data_off_only_aggregate_tools_are_offered_and_notes_stay_local()
    {
        var (app, _) = await ClinicWithDebtorAsync(AiPatientDataMode.Off);
        using var __ = app;

        await app.Get<AssistantService>().AskAsync(new AssistantRequest("Who owes us money?", null), CancellationToken.None);
        Assert.All(app.Ai.OfferedTools, t => Assert.Contains(t, AssistantTools.AggregateTools));
        Assert.DoesNotContain("find_patient", app.Ai.OfferedTools);

        var note = await app.Get<ClinicalNoteAiService>().StructureAsync(
            new StructureNoteRequest("Patient complains of pain in upper right molar. Caries visible."), CancellationToken.None);
        Assert.Null(app.Ai.LastJsonInput);       // nothing was sent to the provider
        Assert.NotEqual("llm", note.Source);
    }

    [Fact]
    public async Task Full_mode_is_an_explicit_choice_and_unknown_settings_fall_back_to_minimised()
    {
        var (app, _) = await ClinicWithDebtorAsync(AiPatientDataMode.Full);
        using var __ = app;
        await app.Get<AssistantService>().AskAsync(new AssistantRequest("Who owes us money?", null), CancellationToken.None);
        Assert.Contains("Sara Haddad", string.Join("\n", app.Ai.ToolResults));

        AiPatientDataMode Mode(string? setting) => new AiDataPolicy(Options.Create(new AiOptions { PatientData = setting! })).PatientData;
        Assert.Equal(AiPatientDataMode.Minimized, Mode("minimized"));
        Assert.Equal(AiPatientDataMode.Minimized, Mode(""));
        Assert.Equal(AiPatientDataMode.Minimized, Mode("everything"));
        Assert.Equal(AiPatientDataMode.Minimized, Mode(null));
        Assert.Equal(AiPatientDataMode.Off, Mode(" OFF "));
        Assert.Equal(AiPatientDataMode.Full, Mode("full"));
    }

    // ------------------------------------------------------------------ references and tenant isolation

    [Fact]
    public async Task Clinical_records_cannot_point_at_another_patients_or_another_clinics_appointment()
    {
        using var app = new TestApp();
        var other = await app.AddClinicAsync("Other");
        var othersPatient = await app.AddPatientAsync("Other Patient");
        var othersAppointment = new Appointment { PatientId = othersPatient.Id, DentistId = other.DentistId, StartTime = app.Clock.UtcNow.AddDays(-1),
            EndTime = app.Clock.UtcNow.AddDays(-1).AddMinutes(30), Treatment = "Filling" };
        app.Db.Appointments.Add(othersAppointment);
        await app.Db.SaveChangesAsync();

        var clinic = await app.AddClinicAsync("Mine");
        var sara = await app.AddPatientAsync("Sara");
        var omar = await app.AddPatientAsync("Omar", "0795550000");
        var omarsAppointment = new Appointment { PatientId = omar.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddDays(-1),
            EndTime = app.Clock.UtcNow.AddDays(-1).AddMinutes(30), Treatment = "Filling" };
        app.Db.Appointments.Add(omarsAppointment);
        await app.Db.SaveChangesAsync();

        using var scope = app.NewScope();
        var records = scope.ServiceProvider.GetRequiredService<ClinicalRecordsService>();
        ClinicalNoteRequest Note(Guid appointmentId) => new(clinic.DentistId, appointmentId, null, "Pain", null, null, null, null, null, null, null, false);

        await Assert.ThrowsAsync<RequestValidationException>(() => records.AddNoteAsync(sara.Id, Note(othersAppointment.Id), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() => records.AddNoteAsync(sara.Id, Note(omarsAppointment.Id), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() => records.AddTreatmentAsync(sara.Id,
            new TreatmentRecordRequest(null, "Extraction", null, null, null, 30, null, clinic.DentistId, othersAppointment.Id), CancellationToken.None));
        // Another clinic's dentist is invisible too.
        await Assert.ThrowsAsync<RequestValidationException>(() => records.AddTreatmentAsync(sara.Id,
            new TreatmentRecordRequest(null, "Extraction", null, null, null, 30, null, other.DentistId, null), CancellationToken.None));
    }

    [Fact]
    public async Task One_clinic_cannot_read_or_change_another_clinics_records()
    {
        using var app = new TestApp();
        var a = await app.AddClinicAsync("Clinic A");
        var secret = await app.AddPatientAsync("Clinic A Patient");
        var b = await app.AddClinicAsync("Clinic B");

        using var scope = app.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Patients.ToListAsync());
        Assert.Null(await db.Patients.FirstOrDefaultAsync(p => p.Id == secret.Id));
        Assert.Single(await db.Clinics.ToListAsync());
        Assert.Equal(b.Id, (await db.Clinics.SingleAsync()).Id);
        Assert.All(await db.Users.ToListAsync(), u => Assert.Equal(b.Id, u.ClinicId));

        var patients = scope.ServiceProvider.GetRequiredService<Tahona.Application.Features.Patients.PatientService>();
        await Assert.ThrowsAsync<NotFoundException>(() => patients.GetAsync(secret.Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => patients.DeleteAsync(secret.Id, CancellationToken.None));

        // A new record is always stamped with the caller's clinic, whatever the caller tries to set.
        db.Patients.Add(new Patient { FileNumber = "P-1", FullName = "New", Phone = "0790000001", PhoneNormalized = "790000001", RegisteredAt = app.Clock.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(b.Id, (await db.Patients.SingleAsync()).ClinicId);
        Assert.NotEqual(a.Id, b.Id);
    }
}
