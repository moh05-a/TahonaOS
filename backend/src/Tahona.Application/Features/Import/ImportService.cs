using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Import;

/// <summary>
/// The import centre. A file goes through three separate steps:
///  1. Upload: the file is parsed and stored row by row. No clinic record is touched.
///  2. Preview: every row is checked against the chosen mapping and the existing data. Still nothing is written;
///     this is the dry run, and it can be repeated as often as needed.
///  3. Commit: the same checks run again under a lock, then every accepted row is written in one transaction.
///     Either the whole import is applied or none of it, and a committed batch can never be committed twice.
/// A committed batch can be undone while the records it created are still untouched.
/// </summary>
public class ImportService(
    IAppDbContext db,
    ITabularFileReader reader,
    IEnumerable<IImportHandler> handlers,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IClock clock)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxRows = 20_000;
    public const int MaxColumns = 80;
    private const int PreviewRowLimit = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ================================================================== upload

    public async Task<ImportUploadDto> UploadAsync(ImportKind kind, string fileName, Stream content, string? sourceSystem, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw RequestValidationException.For("file", "The file is empty.");
        if (buffer.Length > MaxFileBytes) throw RequestValidationException.For("file", "Files must be 10 MB or smaller. Split a larger export into several files.");
        var bytes = buffer.ToArray();

        var file = reader.Read(new MemoryStream(bytes), fileName);
        if (file.Rows.Count == 0) throw RequestValidationException.For("file", "The file has a header row but no data rows.");
        if (file.Rows.Count > MaxRows) throw RequestValidationException.For("file", $"The file has {file.Rows.Count:N0} rows. Import at most {MaxRows:N0} rows at a time.");
        if (file.Headers.Count > MaxColumns) throw RequestValidationException.For("file", $"The file has {file.Headers.Count} columns; at most {MaxColumns} are supported.");

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var safeName = Path.GetFileName(fileName);
        var batch = new ImportBatch
        {
            Kind = kind,
            FileName = safeName.Length > 260 ? safeName[^260..] : safeName,
            FileHash = hash,
            SourceSystem = string.IsNullOrWhiteSpace(sourceSystem) ? "Spreadsheet" : sourceSystem.Trim() is { Length: > 80 } s ? s[..80] : sourceSystem.Trim(),
            HeadersJson = JsonSerializer.Serialize(file.Headers, Json),
            TotalRows = file.Rows.Count,
            CreatedById = currentUser.UserId
        };
        db.ImportBatches.Add(batch);
        db.ImportRows.AddRange(file.Rows.Select((cells, i) => new ImportRow
        {
            BatchId = batch.Id, RowNumber = i + 1, DataJson = JsonSerializer.Serialize(cells, Json)
        }));
        await db.SaveChangesAsync(ct);
        return await GetAsync(batch.Id, ct);
    }

    /// <summary>A batch with what is needed to map and preview it; also used to resume an unfinished import.</summary>
    public async Task<ImportUploadDto> GetAsync(Guid id, CancellationToken ct)
    {
        var batch = await db.ImportBatches.AsNoTracking().Include(b => b.CreatedBy).FirstOrDefaultAsync(b => b.Id == id, ct)
                    ?? throw new NotFoundException("Import", id);
        var headers = Headers(batch);
        var sample = (await db.ImportRows.AsNoTracking().Where(r => r.BatchId == id).OrderBy(r => r.RowNumber).Take(5).Select(r => r.DataJson).ToListAsync(ct))
            .Select(Cells).ToList();
        var previous = await db.ImportBatches.AsNoTracking()
            .Where(b => b.Id != id && b.Kind == batch.Kind && b.FileHash == batch.FileHash && b.Status == ImportBatchStatus.Committed)
            .MaxAsync(b => b.CommittedAt, ct);

        return new ImportUploadDto(ToDto(batch), headers, sample, ImportDefinitions.SuggestMapping(ImportDefinitions.For(batch.Kind), headers),
            batch.MappingJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(batch.MappingJson, Json),
            batch.OptionsJson is null ? null : JsonSerializer.Deserialize<ImportOptions>(batch.OptionsJson, Json), previous);
    }

    public async Task<IReadOnlyList<ImportBatchDto>> ListAsync(CancellationToken ct) =>
        (await db.ImportBatches.AsNoTracking().Include(b => b.CreatedBy).OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync(ct))
        .Select(ToDto).ToList();

    /// <summary>Discards an upload that was never imported.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Import", id);
        if (batch.Status != ImportBatchStatus.Uploaded)
            throw new BusinessRuleException("An import that was applied is part of the clinic's record and cannot be deleted. Use undo instead.");
        db.ImportRows.RemoveRange(await db.ImportRows.Where(r => r.BatchId == id).ToListAsync(ct));
        db.ImportBatches.Remove(batch);
        await db.SaveChangesAsync(ct);
    }

    // ================================================================== preview (dry run)

    public async Task<ImportPreviewDto> PreviewAsync(Guid id, ImportRunRequest request, CancellationToken ct)
    {
        var run = await EvaluateAsync(id, request, tracked: false, ct);
        if (run.Batch.Status != ImportBatchStatus.Uploaded) throw new ConflictException("This file has already been imported.");

        // Remember the mapping and options so an interrupted import can be resumed. Nothing else is written.
        var stored = await db.ImportBatches.FirstAsync(b => b.Id == id, ct);
        stored.MappingJson = JsonSerializer.Serialize(request.Mapping, Json);
        stored.OptionsJson = JsonSerializer.Serialize(run.Input.Options, Json);
        await db.SaveChangesAsync(ct);

        var ordered = run.Rows
            .OrderBy(r => r.Verdict switch { RowVerdict.Error => 0, RowVerdict.Duplicate => 1, RowVerdict.PossibleDuplicate => 1, RowVerdict.AlreadyImported => 2, _ => r.Warnings.Count > 0 ? 3 : 4 })
            .ThenBy(r => r.RowNumber).ToList();
        return new ImportPreviewDto(Summarise(run.Rows, run.Batch.TotalRows), ordered.Take(PreviewRowLimit).ToList(), ordered.Count > PreviewRowLimit, run.MappingProblems);
    }

    // ================================================================== commit

    public async Task<ImportCommitDto> CommitAsync(Guid id, ImportRunRequest request, CancellationToken ct)
    {
        var clinicId = tenant.ClinicId ?? throw new UnauthorizedException();
        try
        {
            // One import at a time per clinic: two files committed together could each miss the other's new records.
            return await db.WithLocksAsync([$"import:{clinicId}"], async () =>
            {
                var run = await EvaluateAsync(id, request, tracked: true, ct);
                var batch = run.Batch;
                if (batch.Status != ImportBatchStatus.Uploaded) throw new ConflictException("This file has already been imported. Nothing was imported again.");
                if (run.MappingProblems.Count > 0)
                    throw new RequestValidationException(new Dictionary<string, string[]> { ["mapping"] = [.. run.MappingProblems] });

                var applied = await Handler(batch.Kind).ApplyAsync(batch, run.Rows, run.Input, ct);
                var byNumber = run.Rows.ToDictionary(r => r.RowNumber);
                foreach (var row in run.StoredRows)
                {
                    var evaluation = byNumber[row.RowNumber];
                    var messages = evaluation.Errors.Concat(evaluation.Warnings).ToList();
                    if (applied.Outcome.TryGetValue(row.RowNumber, out var outcome))
                    {
                        row.Status = outcome.Status;
                        row.EntityType = outcome.EntityType;
                        row.EntityId = outcome.EntityId;
                    }
                    else if (evaluation.Verdict == RowVerdict.Error) row.Status = ImportRowStatus.Error;
                    else
                    {
                        row.Status = ImportRowStatus.Skipped;
                        if (evaluation.Action == RowActions.FillBlanks) messages.Add("Nothing to add: the existing record already has these details.");
                        else if (evaluation.MatchLabel is not null) messages.Add($"Skipped: matches {evaluation.MatchLabel}.");
                        row.EntityId = evaluation.MatchId;
                        row.EntityType = evaluation.MatchId is null ? null : batch.Kind == ImportKind.Patients ? nameof(Patient) : null;
                    }
                    row.MessagesJson = messages.Count == 0 ? null : JsonSerializer.Serialize(messages, Json);
                }

                batch.Status = ImportBatchStatus.Committed;
                batch.CommittedAt = clock.UtcNow;
                batch.CommittedById = currentUser.UserId;
                batch.MappingJson = JsonSerializer.Serialize(request.Mapping, Json);
                batch.OptionsJson = JsonSerializer.Serialize(run.Input.Options, Json);
                batch.CreatedCount = run.StoredRows.Count(r => r.Status == ImportRowStatus.Created);
                batch.UpdatedCount = run.StoredRows.Count(r => r.Status == ImportRowStatus.Updated);
                batch.SkippedCount = run.StoredRows.Count(r => r.Status == ImportRowStatus.Skipped);
                batch.ErrorCount = run.StoredRows.Count(r => r.Status == ImportRowStatus.Error);
                batch.TotalAmount = applied.TotalAmount;
                batch.LastError = null;
                await db.SaveChangesAsync(ct);

                var summary = Summarise(run.Rows, batch.TotalRows) with
                {
                    WillCreate = batch.CreatedCount, WillUpdate = batch.UpdatedCount, WillSkip = batch.SkippedCount, TotalAmount = applied.TotalAmount
                };
                return new ImportCommitDto(ToDto(batch), summary);
            }, ct);
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
        {
            // The transaction rolled back, so no record was created. Note the failure on the batch; it stays ready to retry.
            await NoteFailureAsync(id, ct);
            throw;
        }
    }

    private async Task NoteFailureAsync(Guid id, CancellationToken ct)
    {
        try
        {
            if (db is DbContext context) context.ChangeTracker.Clear();
            var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (batch is null || batch.Status != ImportBatchStatus.Uploaded) return;
            batch.LastError = $"The import failed at {clock.UtcNow:yyyy-MM-dd HH:mm} UTC and nothing was imported. It can be retried.";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Recording the failure is best effort; the original error is what matters.
        }
    }

    // ================================================================== undo

    /// <summary>
    /// Removes what a committed batch created, as long as nobody has built on it. A record that has since been used
    /// (a patient with an appointment, an invoice with a payment) is kept and listed, never deleted.
    /// </summary>
    public async Task<ImportUndoDto> UndoAsync(Guid id, CancellationToken ct)
    {
        var clinicId = tenant.ClinicId ?? throw new UnauthorizedException();
        return await db.WithLocksAsync([$"import:{clinicId}"], async () =>
        {
            var batch = await db.ImportBatches.Include(b => b.CreatedBy).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Import", id);
            if (batch.Status != ImportBatchStatus.Committed) throw new BusinessRuleException("Only an applied import can be undone.");

            var notes = new List<string>();
            var (removed, kept) = batch.Kind switch
            {
                ImportKind.Patients => await UndoPatientsAsync(batch, notes, ct),
                ImportKind.Appointments => await UndoAppointmentsAsync(batch, notes, ct),
                ImportKind.TreatmentPlans => await UndoPlansAsync(batch, notes, ct),
                _ => await UndoBalancesAsync(batch, notes, ct)
            };
            if (batch.UpdatedCount > 0)
                notes.Add($"{batch.UpdatedCount} existing record(s) had empty fields filled in by this import. Those additions are kept.");

            // With records left in place the import is still partly in effect, so it stays "applied".
            if (kept == 0)
            {
                batch.Status = ImportBatchStatus.Undone;
                batch.UndoneAt = clock.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return new ImportUndoDto(ToDto(batch), removed, kept, notes);
        }, ct);
    }

    private async Task<(int Removed, int Kept)> UndoPatientsAsync(ImportBatch batch, List<string> notes, CancellationToken ct)
    {
        var patients = await db.Patients.Where(p => p.ImportBatchId == batch.Id).ToListAsync(ct);
        var ids = patients.Select(p => p.Id).ToList();
        var used = new HashSet<Guid>();
        used.UnionWith(await db.Appointments.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.Invoices.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.TreatmentPlans.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.TreatmentRecords.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.ClinicalNotes.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.Messages.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.Documents.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));
        used.UnionWith(await db.PatientNotes.Where(x => ids.Contains(x.PatientId)).Select(x => x.PatientId).Distinct().ToListAsync(ct));

        var removable = patients.Where(p => !used.Contains(p.Id)).ToList();
        foreach (var p in removable)
        {
            // Free the file number and the source reference, so the corrected file can be imported again.
            p.FileNumber = $"X-{Guid.NewGuid():N}"[..20];
            p.ExternalId = null;
            db.Patients.Remove(p); // soft delete
        }
        if (used.Count > 0)
            notes.Add($"{used.Count} patient(s) now have appointments, invoices, notes or messages and were kept.");
        return (removable.Count, used.Count);
    }

    private async Task<(int Removed, int Kept)> UndoAppointmentsAsync(ImportBatch batch, List<string> notes, CancellationToken ct)
    {
        var appointments = await db.Appointments.Where(a => a.ImportBatchId == batch.Id).ToListAsync(ct);
        var ids = appointments.Select(a => a.Id).ToList();
        var used = new HashSet<Guid>();
        used.UnionWith(await db.TreatmentRecords.Where(x => x.AppointmentId != null && ids.Contains(x.AppointmentId.Value)).Select(x => x.AppointmentId!.Value).ToListAsync(ct));
        used.UnionWith(await db.ClinicalNotes.Where(x => x.AppointmentId != null && ids.Contains(x.AppointmentId.Value)).Select(x => x.AppointmentId!.Value).ToListAsync(ct));
        used.UnionWith(await db.Messages.Where(x => x.AppointmentId != null && ids.Contains(x.AppointmentId.Value)).Select(x => x.AppointmentId!.Value).ToListAsync(ct));
        // An appointment changed after the import (rescheduled, checked in…) belongs to the clinic's own work now.
        var kept = appointments.Where(a => used.Contains(a.Id) || a.UpdatedAt is not null).ToList();
        var removable = appointments.Except(kept).ToList();

        var patientIds = removable.Select(a => a.PatientId).Distinct().ToList();
        var patients = await db.Patients.Where(p => patientIds.Contains(p.Id)).ToListAsync(ct);
        var removedIds = removable.Select(a => a.Id).ToHashSet();
        foreach (var patient in patients)
        {
            // If the last-visit date came from an appointment being removed, fall back to what remains.
            if (patient.LastVisitAt is null || !removable.Any(a => a.PatientId == patient.Id && a.StartTime == patient.LastVisitAt)) continue;
            var lastAppointment = await db.Appointments.Where(a => a.PatientId == patient.Id && a.Status == AppointmentStatus.Completed && !removedIds.Contains(a.Id))
                .MaxAsync(a => (DateTime?)a.StartTime, ct);
            var lastTreatment = await db.TreatmentRecords.Where(r => r.PatientId == patient.Id).MaxAsync(r => (DateTime?)r.PerformedAt, ct);
            patient.LastVisitAt = lastAppointment > lastTreatment || lastTreatment is null ? lastAppointment : lastTreatment;
        }

        db.Appointments.RemoveRange(removable);
        if (kept.Count > 0) notes.Add($"{kept.Count} appointment(s) were changed or used after the import and were kept.");
        return (removable.Count, kept.Count);
    }

    private async Task<(int Removed, int Kept)> UndoPlansAsync(ImportBatch batch, List<string> notes, CancellationToken ct)
    {
        var plans = await db.TreatmentPlans.Include(p => p.Items).Where(p => p.ImportBatchId == batch.Id).ToListAsync(ct);
        var ids = plans.Select(p => p.Id).ToList();
        var itemIds = plans.SelectMany(p => p.Items).Select(i => i.Id).ToList();
        var used = new HashSet<Guid>();
        used.UnionWith(await db.Appointments.Where(x => x.TreatmentPlanId != null && ids.Contains(x.TreatmentPlanId.Value)).Select(x => x.TreatmentPlanId!.Value).ToListAsync(ct));
        used.UnionWith(await db.Invoices.Where(x => x.TreatmentPlanId != null && ids.Contains(x.TreatmentPlanId.Value)).Select(x => x.TreatmentPlanId!.Value).ToListAsync(ct));
        var treated = await db.TreatmentRecords.Where(x => x.TreatmentPlanItemId != null && itemIds.Contains(x.TreatmentPlanItemId.Value))
            .Select(x => x.TreatmentPlanItemId!.Value).ToListAsync(ct);
        // A plan edited, booked, invoiced or treated since the import is the clinic's own work now.
        var kept = plans.Where(p => used.Contains(p.Id) || p.UpdatedAt is not null || p.Items.Any(i => treated.Contains(i.Id) || i.UpdatedAt is not null)).ToList();
        var removable = plans.Except(kept).ToList();
        db.TreatmentPlans.RemoveRange(removable);
        if (kept.Count > 0) notes.Add($"{kept.Count} plan(s) were changed, booked, invoiced or treated after the import and were kept.");
        return (removable.Count, kept.Count);
    }

    private async Task<(int Removed, int Kept)> UndoBalancesAsync(ImportBatch batch, List<string> notes, CancellationToken ct)
    {
        var invoices = await db.Invoices.Include(i => i.Payments).Where(i => i.ImportBatchId == batch.Id && i.Status != InvoiceStatus.Void).ToListAsync(ct);
        var paid = invoices.Where(i => i.Payments.Count > 0).ToList();
        var removable = invoices.Except(paid).ToList();
        foreach (var invoice in removable)
        {
            // Financial records are voided, not deleted: the number and the history stay.
            invoice.Status = InvoiceStatus.Void;
            invoice.Balance = 0;
            invoice.Notes = $"{invoice.Notes}\nVOID: import undone.".Trim();
        }
        if (paid.Count > 0) notes.Add($"{paid.Count} opening balance(s) already have payments recorded against them and were kept.");
        return (removable.Count, paid.Count);
    }

    // ================================================================== report

    /// <summary>
    /// Every source row with its outcome, as CSV: the original cells plus the result and the reasons. For a file not
    /// yet imported this is the dry-run result; rows with problems can be corrected in the report and uploaded again.
    /// </summary>
    public async Task<(byte[] Content, string FileName)> ReportAsync(Guid id, bool problemsOnly, CancellationToken ct)
    {
        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Import", id);
        var headers = Headers(batch);
        var stored = await db.ImportRows.AsNoTracking().Where(r => r.BatchId == id).OrderBy(r => r.RowNumber).ToListAsync(ct);

        var lines = new List<IEnumerable<string?>> { new[] { "Row", "Result", "Details" }.Concat(headers) };
        if (batch.Status == ImportBatchStatus.Uploaded)
        {
            if (batch.MappingJson is null) throw new BusinessRuleException("Preview the import first; the report shows the result of the preview.");
            var request = new ImportRunRequest(JsonSerializer.Deserialize<Dictionary<string, string?>>(batch.MappingJson, Json)!,
                batch.OptionsJson is null ? null : JsonSerializer.Deserialize<ImportOptions>(batch.OptionsJson, Json), null);
            var run = await EvaluateAsync(id, request, tracked: false, ct);
            var byNumber = run.Rows.ToDictionary(r => r.RowNumber);
            foreach (var row in stored)
            {
                if (!byNumber.TryGetValue(row.RowNumber, out var e)) continue;
                if (problemsOnly && e.Verdict == RowVerdict.Ready && e.Warnings.Count == 0) continue;
                var result = e.Verdict == RowVerdict.Error ? "Error" : e.Action == RowActions.Create ? "Will be imported"
                    : e.Action == RowActions.FillBlanks ? "Will fill empty fields" : "Will be skipped";
                lines.Add(new[] { row.RowNumber.ToString(), result, string.Join(" | ", e.Errors.Concat(e.Warnings)) }.Concat(Cells(row.DataJson)));
            }
        }
        else
        {
            foreach (var row in stored)
            {
                if (problemsOnly && row.Status == ImportRowStatus.Created && row.MessagesJson is null) continue;
                var messages = row.MessagesJson is null ? [] : JsonSerializer.Deserialize<List<string>>(row.MessagesJson, Json)!;
                lines.Add(new[] { row.RowNumber.ToString(), row.Status.ToString(), string.Join(" | ", messages) }.Concat(Cells(row.DataJson)));
            }
        }
        var name = Path.GetFileNameWithoutExtension(batch.FileName);
        return (CsvText.Write(lines), $"{name}-import-report.csv");
    }

    // ================================================================== evaluation

    private sealed record Run(ImportBatch Batch, ImportInput Input, IReadOnlyList<RowEvaluation> Rows, List<ImportRow> StoredRows, List<string> MappingProblems);

    private async Task<Run> EvaluateAsync(Guid id, ImportRunRequest request, bool tracked, CancellationToken ct)
    {
        var batches = tracked ? db.ImportBatches : db.ImportBatches.AsNoTracking();
        var batch = await batches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Import", id);
        var rowQuery = tracked ? db.ImportRows : db.ImportRows.AsNoTracking();
        var stored = await rowQuery.Where(r => r.BatchId == id).OrderBy(r => r.RowNumber).ToListAsync(ct);

        var definition = ImportDefinitions.For(batch.Kind);
        var headers = Headers(batch);
        var problems = new List<string>();
        var mapping = new Dictionary<string, int>();
        foreach (var (field, header) in request.Mapping ?? [])
        {
            if (string.IsNullOrWhiteSpace(header) || definition.Fields.All(f => f.Key != field)) continue;
            var index = headers.FindIndex(h => h.Equals(header, StringComparison.OrdinalIgnoreCase));
            if (index < 0) problems.Add($"The file has no column named '{header}'.");
            else mapping[field] = index;
        }
        foreach (var field in definition.Fields.Where(f => f.Required && !mapping.ContainsKey(f.Key)))
            problems.Add($"Choose the column that contains '{field.Label}'.");
        foreach (var group in definition.RequiredAnyOf.Where(g => !g.Any(mapping.ContainsKey)))
            problems.Add($"Choose a column for at least one of: {string.Join(", ", group.Select(k => definition.Fields.First(f => f.Key == k).Label))}.");

        var options = request.Options ?? new ImportOptions();
        if (options.DefaultDentistId is not null && !await db.Dentists.AnyAsync(d => d.Id == options.DefaultDentistId, ct))
            problems.Add("The default dentist was not found.");

        var input = new ImportInput(stored.Select(r => new SourceRow(r.RowNumber, Cells(r.DataJson), mapping)).ToList(), options,
            request.Decisions ?? [], batch.SourceSystem);
        var rows = problems.Count > 0 ? [] : await Handler(batch.Kind).EvaluateAsync(input, ct);
        return new Run(batch, input, rows, stored, problems);
    }

    private static ImportSummaryDto Summarise(IReadOnlyList<RowEvaluation> rows, int total) => new(
        total,
        rows.Count(r => r.Verdict == RowVerdict.Ready),
        rows.Count(r => r.Verdict == RowVerdict.Duplicate),
        rows.Count(r => r.Verdict == RowVerdict.PossibleDuplicate),
        rows.Count(r => r.Verdict == RowVerdict.AlreadyImported),
        rows.Count(r => r.Verdict == RowVerdict.Error),
        rows.Count(r => r.Verdict != RowVerdict.Error && r.Action == RowActions.Create),
        rows.Count(r => r.Verdict != RowVerdict.Error && r.Action == RowActions.FillBlanks),
        rows.Count(r => r.Verdict == RowVerdict.Error || r.Action == RowActions.Skip),
        rows.Count(r => r.Warnings.Count > 0),
        rows.Any(r => r.Amount is not null) ? rows.Where(r => r.Verdict != RowVerdict.Error && r.Action == RowActions.Create).Sum(r => r.Amount ?? 0) : null);

    private IImportHandler Handler(ImportKind kind) => handlers.First(h => h.Kind == kind);

    private static List<string> Headers(ImportBatch batch) => JsonSerializer.Deserialize<List<string>>(batch.HeadersJson, Json) ?? [];

    private static List<string> Cells(string json) => JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    private static ImportBatchDto ToDto(ImportBatch b) => new(b.Id, b.Kind, b.FileName, b.SourceSystem, b.Status, b.TotalRows, b.CreatedCount,
        b.UpdatedCount, b.SkippedCount, b.ErrorCount, b.TotalAmount, b.CreatedAt, b.CreatedBy?.FullName, b.CommittedAt, b.UndoneAt, b.LastError);
}
