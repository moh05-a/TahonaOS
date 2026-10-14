using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Import;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Export;

public record DataExport(Stream Content, string FileName);

/// <summary>
/// A complete, portable copy of one clinic's records: a ZIP of CSV files that open in any spreadsheet, plus a
/// manifest describing every file and column. Identifiers are kept so the relationships between files survive,
/// and nothing is transformed beyond formatting. Password hashes, sessions and other clinics' data are never included.
/// </summary>
public class DataExportService(IAppDbContext db, IFileStorage storage, ICurrentUser currentUser, IClock clock)
{
    public const int FormatVersion = 1;

    private sealed record Sheet(string File, string Description, string[] Columns, List<string?[]> Rows);

    public async Task<DataExport> ExportAsync(bool includeDocuments, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstAsync(ct);
        var now = clock.UtcNow;
        var sheets = await BuildSheetsAsync(clinic, ct);

        // Written to a temporary file that removes itself when the download finishes.
        var temp = new FileStream(Path.Combine(Path.GetTempPath(), $"tahona-export-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var documentPaths = new Dictionary<Guid, string>();
            var missingDocuments = 0;
            using (var zip = new ZipArchive(temp, ZipArchiveMode.Create, leaveOpen: true))
            {
                if (includeDocuments)
                {
                    var documents = await db.Documents.AsNoTracking().Select(d => new { d.Id, d.FileName, d.StoragePath, File = d.Patient.FileNumber }).ToListAsync(ct);
                    foreach (var d in documents)
                    {
                        await using var source = await storage.OpenReadAsync(d.StoragePath, ct);
                        if (source is null) { missingDocuments++; continue; }
                        var path = $"documents/{Safe(d.File)}/{d.Id:N}_{Safe(d.FileName)}";
                        await using var entry = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                        await source.CopyToAsync(entry, ct);
                        documentPaths[d.Id] = path;
                    }
                }

                // The documents sheet says where each file is inside the archive.
                var documentSheet = sheets.First(s => s.File == "documents.csv");
                foreach (var row in documentSheet.Rows)
                    row[^1] = documentPaths.GetValueOrDefault(Guid.Parse(row[0]!));

                foreach (var sheet in sheets)
                    Write(zip, sheet.File, CsvText.Write(sheet.Rows.Prepend(sheet.Columns)));

                var exclusions = new List<string>
                {
                    "Staff passwords and sign-in sessions are never exported.",
                    "Patients deleted in TahonaOS are not included.",
                    "The audit log is not included; ask support if you need it.",
                    includeDocuments ? "Uploaded files are in the documents/ folder." : "Uploaded files (X-rays, scans) were not requested; documents.csv lists them."
                };
                if (missingDocuments > 0) exclusions.Add($"{missingDocuments} uploaded file(s) could not be read from storage and are missing from this export.");

                var manifest = new
                {
                    format = "tahona-export",
                    version = FormatVersion,
                    generatedAt = now,
                    generatedBy = currentUser.FullName,
                    clinic = new { clinic.Id, clinic.Name, clinic.NameAr, clinic.TimeZone, clinic.Currency },
                    conventions = new[]
                    {
                        "CSV files are UTF-8 with a byte-order mark, comma separated, one header row.",
                        "Dates and times are UTC in ISO 8601 (2026-10-12T09:30:00Z). Dates without a time are yyyy-MM-dd.",
                        $"Money is in {clinic.Currency} with a dot as decimal separator. Refunds are positive amounts with type = Refund.",
                        "Columns ending in _id refer to the id column of the named file, so relationships between files are preserved.",
                        "A cell starting with = + - or @ is prefixed with an apostrophe so spreadsheets do not treat it as a formula."
                    },
                    files = sheets.Select(s => new { file = s.File, description = s.Description, rows = s.Rows.Count, columns = s.Columns }),
                    exclusions
                };
                Write(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));
                Write(zip, "README.txt", Encoding.UTF8.GetBytes(Readme(clinic, now, sheets)));
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId, UserName = currentUser.FullName ?? "System", Action = "DataExported", EntityType = nameof(Clinic), EntityId = clinic.Id,
                Summary = $"Full data export ({sheets.Sum(s => s.Rows.Count):N0} rows{(includeDocuments ? $", {documentPaths.Count} files" : "")})",
                Timestamp = now, IpAddress = currentUser.IpAddress
            });
            await db.SaveChangesAsync(ct);

            temp.Position = 0;
            return new DataExport(temp, $"tahona-export-{clinic.Slug}-{now:yyyyMMdd-HHmm}.zip");
        }
        catch
        {
            await temp.DisposeAsync();
            throw;
        }
    }

    private async Task<List<Sheet>> BuildSheetsAsync(Clinic clinic, CancellationToken ct)
    {
        var sheets = new List<Sheet>();
        void Add(string file, string description, string[] columns, IEnumerable<string?[]> rows) => sheets.Add(new Sheet(file, description, columns, rows.ToList()));

        Add("clinic.csv", "The clinic profile.",
            ["id", "name", "name_ar", "phone", "email", "address", "city", "country", "time_zone", "currency", "default_language"],
            [[G(clinic.Id), clinic.Name, clinic.NameAr, clinic.Phone, clinic.Email, clinic.Address, clinic.City, clinic.Country, clinic.TimeZone, clinic.Currency, clinic.DefaultLanguage]]);

        Add("staff.csv", "Staff accounts and their roles. Passwords are not exported.",
            ["id", "email", "full_name", "full_name_ar", "phone", "role", "is_active", "last_login_at", "created_at"],
            (await db.Users.AsNoTracking().Include(u => u.Role).OrderBy(u => u.FullName).ToListAsync(ct))
            .Select(u => new[] { G(u.Id), u.Email, u.FullName, u.FullNameAr, u.Phone, u.Role.Name, B(u.IsActive), T(u.LastLoginAt), T(u.CreatedAt) }));

        Add("dentists.csv", "Dentist profiles. user_id links to staff.csv when the dentist signs in.",
            ["id", "full_name", "full_name_ar", "specialty", "phone", "email", "license_number", "is_active", "user_id"],
            (await db.Dentists.AsNoTracking().OrderBy(d => d.FullName).ToListAsync(ct))
            .Select(d => new[] { G(d.Id), d.FullName, d.FullNameAr, d.Specialty, d.Phone, d.Email, d.LicenseNumber, B(d.IsActive), G(d.UserId) }));

        Add("procedures.csv", "The clinic's procedure and price list.",
            ["id", "code", "name", "name_ar", "category", "default_price", "default_duration_minutes", "is_active"],
            (await db.Procedures.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct))
            .Select(p => new[] { G(p.Id), p.Code, p.Name, p.NameAr, p.Category.ToString(), M(p.DefaultPrice), p.DefaultDurationMinutes.ToString(), B(p.IsActive) }));

        Add("patients.csv", "Patients. external_id and source_system are the identifiers kept from an import.",
            ["id", "file_number", "full_name", "full_name_ar", "date_of_birth", "gender", "phone", "whatsapp_number", "email", "address", "city", "national_id",
             "emergency_contact_name", "emergency_contact_phone", "emergency_contact_relation", "allergies", "medical_conditions", "current_medications",
             "medical_notes", "insurance_provider", "insurance_policy_number", "insurance_coverage_percent", "preferred_language", "status",
             "messaging_consent", "messaging_consent_at", "registered_at", "last_visit_at", "primary_dentist_id", "external_id", "source_system"],
            (await db.Patients.AsNoTracking().OrderBy(p => p.FileNumber).ToListAsync(ct))
            .Select(p => new[]
            {
                G(p.Id), p.FileNumber, p.FullName, p.FullNameAr, p.DateOfBirth?.ToString("yyyy-MM-dd"), p.Gender?.ToString(), p.Phone, p.WhatsAppNumber, p.Email,
                p.Address, p.City, p.NationalId, p.EmergencyContactName, p.EmergencyContactPhone, p.EmergencyContactRelation, p.Allergies, p.MedicalConditions,
                p.CurrentMedications, p.MedicalNotes, p.InsuranceProvider, p.InsurancePolicyNumber, p.InsuranceCoveragePercent?.ToString(CultureInfo.InvariantCulture),
                p.PreferredLanguage, p.Status.ToString(), p.MessagingConsent.ToString(), T(p.MessagingConsentAt), T(p.RegisteredAt), T(p.LastVisitAt),
                G(p.PrimaryDentistId), p.ExternalId, p.SourceSystem
            }));

        Add("patient_notes.csv", "Non-clinical staff notes on patients.",
            ["id", "patient_id", "content", "is_pinned", "author_id", "created_at"],
            (await db.PatientNotes.AsNoTracking().OrderBy(n => n.CreatedAt).ToListAsync(ct))
            .Select(n => new[] { G(n.Id), G(n.PatientId), n.Content, B(n.IsPinned), G(n.AuthorId), T(n.CreatedAt) }));

        Add("appointments.csv", "Every appointment with its status.",
            ["id", "patient_id", "dentist_id", "start_time", "end_time", "treatment", "status", "notes", "chair", "treatment_plan_id", "confirmed_at", "arrived_at",
             "completed_at", "cancelled_at", "cancellation_reason", "external_id"],
            (await db.Appointments.AsNoTracking().OrderBy(a => a.StartTime).ToListAsync(ct))
            .Select(a => new[]
            {
                G(a.Id), G(a.PatientId), G(a.DentistId), T(a.StartTime), T(a.EndTime), a.Treatment, a.Status.ToString(), a.Notes, a.Chair, G(a.TreatmentPlanId),
                T(a.ConfirmedAt), T(a.ArrivedAt), T(a.CompletedAt), T(a.CancelledAt), a.CancellationReason, a.ExternalId
            }));

        Add("treatment_plans.csv", "Treatment plans. Status separates proposed, accepted, in-progress and completed plans.",
            ["id", "plan_number", "patient_id", "dentist_id", "title", "status", "notes", "discount_amount", "total_amount", "created_at", "presented_at", "accepted_at",
             "started_at", "completed_at", "rejected_at", "rejection_reason"],
            (await db.TreatmentPlans.AsNoTracking().OrderBy(t => t.PlanNumber).ToListAsync(ct))
            .Select(t => new[]
            {
                G(t.Id), t.PlanNumber, G(t.PatientId), G(t.DentistId), t.Title, t.Status.ToString(), t.Notes, M(t.DiscountAmount), M(t.TotalAmount), T(t.CreatedAt),
                T(t.PresentedAt), T(t.AcceptedAt), T(t.StartedAt), T(t.CompletedAt), T(t.RejectedAt), t.RejectionReason
            }));

        Add("treatment_plan_items.csv", "The procedures inside each plan and whether each was completed.",
            ["id", "treatment_plan_id", "sequence", "procedure_id", "name", "tooth_number", "surfaces", "quantity", "unit_price", "discount", "total", "status",
             "completed_at", "appointment_id", "is_invoiced"],
            (await db.TreatmentPlanItems.AsNoTracking().OrderBy(i => i.TreatmentPlanId).ThenBy(i => i.Sequence).ToListAsync(ct))
            .Select(i => new[]
            {
                G(i.Id), G(i.TreatmentPlanId), i.Sequence.ToString(), G(i.ProcedureId), i.Name, i.ToothNumber?.ToString(), i.Surfaces, i.Quantity.ToString(),
                M(i.UnitPrice), M(i.Discount), M(i.Total), i.Status.ToString(), T(i.CompletedAt), G(i.AppointmentId), B(i.IsInvoiced)
            }));

        Add("treatment_records.csv", "Treatment actually performed (the clinical history).",
            ["id", "patient_id", "dentist_id", "performed_at", "procedure_id", "procedure_name", "category", "tooth_number", "surfaces", "price", "notes",
             "appointment_id", "treatment_plan_item_id"],
            (await db.TreatmentRecords.AsNoTracking().OrderBy(r => r.PerformedAt).ToListAsync(ct))
            .Select(r => new[]
            {
                G(r.Id), G(r.PatientId), G(r.DentistId), T(r.PerformedAt), G(r.ProcedureId), r.ProcedureName, r.Category?.ToString(), r.ToothNumber?.ToString(),
                r.Surfaces, M(r.Price), r.Notes, G(r.AppointmentId), G(r.TreatmentPlanItemId)
            }));

        Add("dental_chart.csv", "Conditions recorded on the dental chart, by FDI tooth number.",
            ["id", "patient_id", "tooth_number", "surfaces", "type", "status", "notes", "recorded_at", "dentist_id", "treatment_plan_item_id"],
            (await db.DentalConditions.AsNoTracking().OrderBy(c => c.PatientId).ThenBy(c => c.ToothNumber).ToListAsync(ct))
            .Select(c => new[]
            {
                G(c.Id), G(c.PatientId), c.ToothNumber.ToString(), c.Surfaces, c.Type.ToString(), c.Status.ToString(), c.Notes, T(c.RecordedAt), G(c.DentistId),
                G(c.TreatmentPlanItemId)
            }));

        Add("clinical_notes.csv", "Clinical notes written by dentists.",
            ["id", "patient_id", "dentist_id", "note_date", "appointment_id", "chief_complaint", "diagnosis", "clinical_findings", "treatment_performed",
             "recommendations", "prescription_notes", "follow_up_instructions", "free_text", "is_ai_assisted"],
            (await db.ClinicalNotes.AsNoTracking().OrderBy(n => n.NoteDate).ToListAsync(ct))
            .Select(n => new[]
            {
                G(n.Id), G(n.PatientId), G(n.DentistId), T(n.NoteDate), G(n.AppointmentId), n.ChiefComplaint, n.Diagnosis, n.ClinicalFindings, n.TreatmentPerformed,
                n.Recommendations, n.PrescriptionNotes, n.FollowUpInstructions, n.FreeText, B(n.IsAiAssisted)
            }));

        Add("invoices.csv", "Invoices. balance is what is still owed; is_opening_balance marks debts carried over from a previous system.",
            ["id", "invoice_number", "patient_id", "dentist_id", "treatment_plan_id", "issue_date", "due_date", "status", "subtotal", "discount_amount", "tax_amount",
             "total", "amount_paid", "balance", "is_opening_balance", "notes"],
            (await db.Invoices.AsNoTracking().OrderBy(i => i.IssueDate).ToListAsync(ct))
            .Select(i => new[]
            {
                G(i.Id), i.InvoiceNumber, G(i.PatientId), G(i.DentistId), G(i.TreatmentPlanId), T(i.IssueDate), T(i.DueDate), i.Status.ToString(), M(i.Subtotal),
                M(i.DiscountAmount), M(i.TaxAmount), M(i.Total), M(i.AmountPaid), M(i.Balance), B(i.IsOpeningBalance), i.Notes
            }));

        Add("invoice_items.csv", "The lines of each invoice.",
            ["id", "invoice_id", "description", "procedure_id", "treatment_plan_item_id", "tooth_number", "quantity", "unit_price", "discount", "total"],
            (await db.InvoiceItems.AsNoTracking().OrderBy(i => i.InvoiceId).ToListAsync(ct))
            .Select(i => new[]
            {
                G(i.Id), G(i.InvoiceId), i.Description, G(i.ProcedureId), G(i.TreatmentPlanItemId), i.ToothNumber?.ToString(), i.Quantity.ToString(), M(i.UnitPrice),
                M(i.Discount), M(i.Total)
            }));

        Add("payments.csv", "Payments and refunds as recorded by staff. A refund has type = Refund and refund_of_payment_id set. "
                            + "An entry with cancelled_at set was made by mistake and cancelled; it counts towards no total.",
            ["id", "receipt_number", "patient_id", "invoice_id", "type", "amount", "method", "paid_at", "reference", "notes", "received_by_id", "refund_of_payment_id",
                "cancelled_at", "cancelled_by_id", "cancel_reason"],
            (await db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.ClinicId == clinic.Id).OrderBy(p => p.PaidAt).ToListAsync(ct))
            .Select(p => new[]
            {
                G(p.Id), p.ReceiptNumber, G(p.PatientId), G(p.InvoiceId), p.Type.ToString(), M(p.Amount), p.Method.ToString(), T(p.PaidAt), p.Reference, p.Notes,
                G(p.ReceivedById), G(p.RefundOfPaymentId), p.IsDeleted ? T(p.DeletedAt) : null, G(p.CancelledById), p.CancelReason
            }));

        Add("expenses.csv", "Clinic expenses.",
            ["id", "expense_date", "category", "description", "amount", "vendor", "payment_method", "reference"],
            (await db.Expenses.AsNoTracking().OrderBy(e => e.ExpenseDate).ToListAsync(ct))
            .Select(e => new[] { G(e.Id), T(e.ExpenseDate), e.Category.ToString(), e.Description, M(e.Amount), e.Vendor, e.PaymentMethod.ToString(), e.Reference }));

        Add("follow_ups.csv", "Follow-up tasks: why the patient was listed, what was done and the recorded outcome.",
            ["id", "patient_id", "issue_type", "title", "status", "priority", "estimated_value", "due_date", "contact_attempts", "last_contacted_at", "outcome",
             "resolved_at", "collected_after_follow_up", "source_entity_type", "source_entity_id", "created_at"],
            (await db.FollowUps.AsNoTracking().OrderBy(f => f.CreatedAt).ToListAsync(ct))
            .Select(f => new[]
            {
                G(f.Id), G(f.PatientId), f.IssueType.ToString(), f.Title, f.Status.ToString(), f.Priority.ToString(), M(f.EstimatedValue), T(f.DueDate),
                f.ContactAttempts.ToString(), T(f.LastContactedAt), f.Outcome, T(f.ResolvedAt), M(f.RecoveredAmount), f.SourceEntityType, G(f.SourceEntityId), T(f.CreatedAt)
            }));

        Add("messages.csv", "Messages to and from patients, and logged phone calls.",
            ["id", "patient_id", "direction", "channel", "status", "body", "to_number", "from_number", "is_automated", "created_at", "sent_at", "delivered_at", "read_at",
             "error", "follow_up_id", "appointment_id"],
            (await db.Messages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync(ct))
            .Select(m => new[]
            {
                G(m.Id), G(m.PatientId), m.Direction.ToString(), m.Channel.ToString(), m.Status.ToString(), m.Body, m.ToNumber, m.FromNumber, B(m.IsAutomated),
                T(m.CreatedAt), T(m.SentAt), T(m.DeliveredAt), T(m.ReadAt), m.Error, G(m.FollowUpId), G(m.AppointmentId)
            }));

        Add("documents.csv", "Uploaded files. path_in_export is filled when the files were included in this export.",
            ["id", "patient_id", "category", "file_name", "content_type", "size_bytes", "description", "tooth_number", "created_at", "path_in_export"],
            (await db.Documents.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync(ct))
            .Select(d => new[]
            {
                G(d.Id), G(d.PatientId), d.Category.ToString(), d.FileName, d.ContentType, d.SizeBytes.ToString(), d.Description, d.ToothNumber?.ToString(), T(d.CreatedAt),
                null
            }));

        return sheets;
    }

    private static string Readme(Clinic clinic, DateTime now, List<Sheet> sheets)
    {
        var text = new StringBuilder();
        text.AppendLine($"TahonaOS data export for {clinic.Name}");
        text.AppendLine($"Generated {now:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine();
        text.AppendLine("Each .csv file opens in Excel, LibreOffice or Google Sheets. manifest.json describes every file and column.");
        text.AppendLine("Columns ending in _id point to the id column of another file (patient_id -> patients.csv).");
        text.AppendLine($"Dates and times are in UTC. Amounts are in {clinic.Currency}.");
        text.AppendLine();
        foreach (var s in sheets) text.AppendLine($"{s.File,-28} {s.Rows.Count,8:N0} rows   {s.Description}");
        return text.ToString();
    }

    private static void Write(ZipArchive zip, string path, byte[] content)
    {
        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        entry.Write(content);
    }

    /// <summary>A file or folder name that is safe inside a ZIP on any operating system.</summary>
    private static string Safe(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.');
        return cleaned.Length == 0 ? "file" : cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }

    private static string? G(Guid? id) => id?.ToString();
    private static string G(Guid id) => id.ToString();
    private static string B(bool value) => value ? "true" : "false";
    private static string M(decimal value) => value.ToString("0.000", CultureInfo.InvariantCulture);
    private static string? T(DateTime? utc) => utc?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
