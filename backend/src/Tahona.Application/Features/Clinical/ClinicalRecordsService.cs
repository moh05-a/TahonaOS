using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Clinical;

public record TreatmentRecordDto(
    Guid Id, string ProcedureName, ProcedureCategory? Category, int? ToothNumber, string? Surfaces, DateTime PerformedAt,
    decimal Price, string? Notes, string DentistName, Guid? TreatmentPlanItemId);

public record TreatmentRecordRequest(
    Guid? ProcedureId, string? ProcedureName, int? ToothNumber, string? Surfaces, DateTimeOffset? PerformedAt,
    decimal? Price, string? Notes, Guid DentistId, Guid? AppointmentId);

public record ClinicalNoteDto(
    Guid Id, DateTime NoteDate, Guid DentistId, string DentistName, Guid? AppointmentId,
    string? ChiefComplaint, string? Diagnosis, string? ClinicalFindings, string? TreatmentPerformed,
    string? Recommendations, string? PrescriptionNotes, string? FollowUpInstructions, string? FreeText,
    bool IsAiAssisted, DateTime CreatedAt);

public record ClinicalNoteRequest(
    Guid? DentistId, Guid? AppointmentId, DateTimeOffset? NoteDate,
    string? ChiefComplaint, string? Diagnosis, string? ClinicalFindings, string? TreatmentPerformed,
    string? Recommendations, string? PrescriptionNotes, string? FollowUpInstructions, string? FreeText,
    bool IsAiAssisted);

public class TreatmentRecordRequestValidator : AbstractValidator<TreatmentRecordRequest>
{
    public TreatmentRecordRequestValidator()
    {
        RuleFor(x => x).Must(x => x.ProcedureId is not null || !string.IsNullOrWhiteSpace(x.ProcedureName))
            .WithMessage("Select a procedure or enter a name.");
        RuleFor(x => x.ProcedureName).MaximumLength(150);
        RuleFor(x => x.ToothNumber).Must(t => t is null || Teeth.IsValid(t.Value)).WithMessage("Invalid FDI tooth number.");
        RuleFor(x => x.Surfaces).MaximumLength(10).Must(Teeth.IsValidSurfaces);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).When(x => x.Price.HasValue);
        RuleFor(x => x.DentistId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class ClinicalNoteRequestValidator : AbstractValidator<ClinicalNoteRequest>
{
    public ClinicalNoteRequestValidator()
    {
        RuleFor(x => x).Must(x => new[] { x.ChiefComplaint, x.Diagnosis, x.ClinicalFindings, x.TreatmentPerformed,
                x.Recommendations, x.PrescriptionNotes, x.FollowUpInstructions, x.FreeText }.Any(s => !string.IsNullOrWhiteSpace(s)))
            .WithMessage("The note is empty.");
        RuleFor(x => x.ChiefComplaint).MaximumLength(4000);
        RuleFor(x => x.Diagnosis).MaximumLength(4000);
        RuleFor(x => x.ClinicalFindings).MaximumLength(4000);
        RuleFor(x => x.TreatmentPerformed).MaximumLength(4000);
        RuleFor(x => x.Recommendations).MaximumLength(4000);
        RuleFor(x => x.PrescriptionNotes).MaximumLength(4000);
        RuleFor(x => x.FollowUpInstructions).MaximumLength(4000);
        RuleFor(x => x.FreeText).MaximumLength(8000);
    }
}

public class ClinicalRecordsService(IAppDbContext db, DentalChartService chart, ICurrentUser currentUser, IClock clock)
{
    // ---------------------------------------------------------------- treatment history

    public async Task<IReadOnlyList<TreatmentRecordDto>> ListTreatmentsAsync(Guid patientId, CancellationToken ct) =>
        await db.TreatmentRecords.AsNoTracking().Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.PerformedAt)
            .Select(r => new TreatmentRecordDto(r.Id, r.ProcedureName, r.Category, r.ToothNumber, r.Surfaces, r.PerformedAt,
                r.Price, r.Notes, r.Dentist.FullName, r.TreatmentPlanItemId))
            .ToListAsync(ct);

    /// <summary>Records a procedure performed outside a treatment plan (e.g. emergency extraction).</summary>
    public async Task<Guid> AddTreatmentAsync(Guid patientId, TreatmentRecordRequest request, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, ct) ?? throw new NotFoundException("Patient", patientId);
        if (!await db.Dentists.AnyAsync(d => d.Id == request.DentistId, ct)) throw RequestValidationException.For("dentistId", "Dentist not found.");
        var procedure = request.ProcedureId is null ? null
            : await db.Procedures.FirstOrDefaultAsync(p => p.Id == request.ProcedureId, ct)
              ?? throw RequestValidationException.For("procedureId", "Procedure not found.");

        await EnsureAppointmentAsync(request.AppointmentId, patientId, ct);

        var now = clock.UtcNow;
        var performedAt = request.PerformedAt?.UtcDateTime ?? now;
        if (performedAt > now.AddMinutes(5)) throw RequestValidationException.For("performedAt", "Treatment date cannot be in the future.");

        var record = new TreatmentRecord
        {
            PatientId = patientId,
            DentistId = request.DentistId,
            AppointmentId = request.AppointmentId,
            ProcedureId = procedure?.Id,
            ProcedureName = string.IsNullOrWhiteSpace(request.ProcedureName) ? procedure!.Name : request.ProcedureName.Trim(),
            Category = procedure?.Category,
            ToothNumber = request.ToothNumber,
            Surfaces = request.Surfaces?.ToUpperInvariant(),
            PerformedAt = performedAt,
            Price = Money.Round(request.Price ?? procedure?.DefaultPrice ?? 0),
            Notes = request.Notes?.Trim()
        };
        db.TreatmentRecords.Add(record);

        if (record.ToothNumber is not null && procedure?.ResultingCondition is not null)
            await chart.RecordTreatmentResultAsync(patientId, record.ToothNumber.Value, procedure.ResultingCondition.Value,
                record.Surfaces, request.DentistId, null, ct);

        if (patient.LastVisitAt is null || patient.LastVisitAt < performedAt) patient.LastVisitAt = performedAt;
        await db.SaveChangesAsync(ct);
        return record.Id;
    }

    // ---------------------------------------------------------------- clinical notes

    public async Task<IReadOnlyList<ClinicalNoteDto>> ListNotesAsync(Guid patientId, CancellationToken ct) =>
        await db.ClinicalNotes.AsNoTracking().Where(n => n.PatientId == patientId)
            .OrderByDescending(n => n.NoteDate)
            .Select(n => new ClinicalNoteDto(n.Id, n.NoteDate, n.DentistId, n.Dentist.FullName, n.AppointmentId,
                n.ChiefComplaint, n.Diagnosis, n.ClinicalFindings, n.TreatmentPerformed, n.Recommendations,
                n.PrescriptionNotes, n.FollowUpInstructions, n.FreeText, n.IsAiAssisted, n.CreatedAt))
            .ToListAsync(ct);

    public async Task<Guid> AddNoteAsync(Guid patientId, ClinicalNoteRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient", patientId);
        var dentistId = await ResolveDentistAsync(request.DentistId, ct);
        await EnsureAppointmentAsync(request.AppointmentId, patientId, ct);

        var note = new ClinicalNote { PatientId = patientId, DentistId = dentistId };
        Apply(note, request);
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task UpdateNoteAsync(Guid patientId, Guid noteId, ClinicalNoteRequest request, CancellationToken ct)
    {
        var note = await db.ClinicalNotes.Include(n => n.Dentist).FirstOrDefaultAsync(n => n.Id == noteId && n.PatientId == patientId, ct)
                   ?? throw new NotFoundException("Clinical note", noteId);
        EnsureAuthorOrAdmin(note);
        await EnsureAppointmentAsync(request.AppointmentId, patientId, ct);
        Apply(note, request);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(Guid patientId, Guid noteId, CancellationToken ct)
    {
        var note = await db.ClinicalNotes.Include(n => n.Dentist).FirstOrDefaultAsync(n => n.Id == noteId && n.PatientId == patientId, ct)
                   ?? throw new NotFoundException("Clinical note", noteId);
        EnsureAuthorOrAdmin(note);
        db.ClinicalNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    private void EnsureAuthorOrAdmin(ClinicalNote note)
    {
        if (note.Dentist.UserId != currentUser.UserId && !currentUser.HasPermission(Permissions.SettingsManage))
            throw new ForbiddenException("Only the authoring dentist or an administrator can change this note.");
    }

    private async Task<Guid> ResolveDentistAsync(Guid? requested, CancellationToken ct)
    {
        if (requested is not null)
        {
            if (!await db.Dentists.AnyAsync(d => d.Id == requested, ct)) throw RequestValidationException.For("dentistId", "Dentist not found.");
            return requested.Value;
        }
        return await db.Dentists.Where(d => d.UserId == currentUser.UserId).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct)
               ?? throw RequestValidationException.For("dentistId", "Select the treating dentist.");
    }

    /// <summary>A linked appointment must exist in this clinic and belong to the same patient.</summary>
    private async Task EnsureAppointmentAsync(Guid? appointmentId, Guid patientId, CancellationToken ct)
    {
        if (appointmentId is not null && !await db.Appointments.AnyAsync(a => a.Id == appointmentId && a.PatientId == patientId, ct))
            throw RequestValidationException.For("appointmentId", "Appointment not found for this patient.");
    }

    private void Apply(ClinicalNote n, ClinicalNoteRequest r)
    {
        n.AppointmentId = r.AppointmentId;
        n.NoteDate = r.NoteDate?.UtcDateTime ?? (n.NoteDate == default ? clock.UtcNow : n.NoteDate);
        n.ChiefComplaint = Clean(r.ChiefComplaint);
        n.Diagnosis = Clean(r.Diagnosis);
        n.ClinicalFindings = Clean(r.ClinicalFindings);
        n.TreatmentPerformed = Clean(r.TreatmentPerformed);
        n.Recommendations = Clean(r.Recommendations);
        n.PrescriptionNotes = Clean(r.PrescriptionNotes);
        n.FollowUpInstructions = Clean(r.FollowUpInstructions);
        n.FreeText = Clean(r.FreeText);
        n.IsAiAssisted = r.IsAiAssisted;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
