using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Clinical;

public record DentalConditionDto(
    Guid Id, int ToothNumber, ToothConditionType Type, ConditionStatus Status, string? Surfaces, string? Notes,
    DateTime RecordedAt, string? DentistName);

public record ToothDto(int ToothNumber, bool IsPrimary, string? Notes, IReadOnlyList<DentalConditionDto> Conditions);

public record ToothHistoryEntryDto(string Kind, string Title, string? Detail, DateTime Date, string? DentistName, decimal? Amount, string? Status);

public record DentalChartDto(Guid PatientId, bool SuggestPrimaryDentition, IReadOnlyList<ToothDto> Teeth);

public record DentalConditionRequest(int ToothNumber, ToothConditionType Type, ConditionStatus Status, string? Surfaces, string? Notes, Guid? DentistId);

public record ToothNoteRequest(string? Notes);

public class DentalConditionValidator : AbstractValidator<DentalConditionRequest>
{
    public DentalConditionValidator()
    {
        RuleFor(x => x.ToothNumber).Must(Teeth.IsValid).WithMessage("Invalid FDI tooth number.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Surfaces).MaximumLength(10).Must(Teeth.IsValidSurfaces).WithMessage("Surfaces must use M, O, D, B, L, I, F.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class DentalChartService(IAppDbContext db, IClock clock, IClinicTime clinicTime, ICurrentUser currentUser)
{
    public async Task<DentalChartDto> GetChartAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Select(p => new { p.Id, p.DateOfBirth }).FirstOrDefaultAsync(p => p.Id == patientId, ct)
                      ?? throw new NotFoundException("Patient", patientId);

        var teeth = await db.DentalTeeth.AsNoTracking().Where(t => t.PatientId == patientId)
            .Select(t => new ToothDto(t.ToothNumber, t.IsPrimary, t.Notes,
                t.Conditions.OrderByDescending(c => c.RecordedAt).Select(c => new DentalConditionDto(
                    c.Id, c.ToothNumber, c.Type, c.Status, c.Surfaces, c.Notes, c.RecordedAt,
                    c.Dentist != null ? c.Dentist.FullName : null)).ToList()))
            .ToListAsync(ct);

        var age = Patients.PatientService.AgeOn(patient.DateOfBirth, clinicTime.Today);
        return new DentalChartDto(patientId, age is < 12, teeth);
    }

    public async Task<IReadOnlyList<ToothHistoryEntryDto>> GetToothHistoryAsync(Guid patientId, int toothNumber, CancellationToken ct)
    {
        var history = new List<ToothHistoryEntryDto>();

        var conditions = await db.DentalConditions.AsNoTracking()
            .Where(c => c.PatientId == patientId && c.ToothNumber == toothNumber)
            .Select(c => new { c.Type, c.Status, c.Surfaces, c.Notes, c.RecordedAt, Dentist = c.Dentist != null ? c.Dentist.FullName : null })
            .ToListAsync(ct);
        history.AddRange(conditions.Select(c => new ToothHistoryEntryDto(
            "condition", c.Type.ToString(), JoinParts(c.Surfaces is null ? null : $"Surfaces {c.Surfaces}", c.Notes),
            c.RecordedAt, c.Dentist, null, c.Status.ToString())));

        var records = await db.TreatmentRecords.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.ToothNumber == toothNumber)
            .Select(r => new { r.ProcedureName, r.Notes, r.PerformedAt, Dentist = r.Dentist.FullName, r.Price })
            .ToListAsync(ct);
        history.AddRange(records.Select(r => new ToothHistoryEntryDto("treatment", r.ProcedureName, r.Notes, r.PerformedAt, r.Dentist, r.Price, "Completed")));

        var planned = await db.TreatmentPlanItems.AsNoTracking()
            .Where(i => i.TreatmentPlan.PatientId == patientId && i.ToothNumber == toothNumber
                        && (i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled)
                        && i.TreatmentPlan.Status != TreatmentPlanStatus.Rejected)
            .Select(i => new { i.Name, i.TreatmentPlan.PlanNumber, i.CreatedAt, i.Total, Dentist = i.TreatmentPlan.Dentist.FullName, i.Status })
            .ToListAsync(ct);
        history.AddRange(planned.Select(i => new ToothHistoryEntryDto("planned", i.Name, $"Plan {i.PlanNumber}", i.CreatedAt, i.Dentist, i.Total, i.Status.ToString())));

        return history.OrderByDescending(h => h.Date).ToList();
    }

    public async Task<DentalConditionDto> AddConditionAsync(Guid patientId, DentalConditionRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient", patientId);
        var tooth = await GetOrCreateToothAsync(patientId, request.ToothNumber, ct);
        var dentistId = request.DentistId ?? await CurrentDentistIdAsync(ct);

        var condition = new DentalCondition
        {
            PatientId = patientId,
            DentalToothId = tooth.Id,
            ToothNumber = request.ToothNumber,
            Type = request.Type,
            Status = request.Status,
            Surfaces = request.Surfaces?.ToUpperInvariant(),
            Notes = request.Notes?.Trim(),
            RecordedAt = clock.UtcNow,
            DentistId = dentistId
        };
        db.DentalConditions.Add(condition);
        await db.SaveChangesAsync(ct);

        var dentistName = dentistId is null ? null : await db.Dentists.Where(d => d.Id == dentistId).Select(d => d.FullName).FirstOrDefaultAsync(ct);
        return new DentalConditionDto(condition.Id, condition.ToothNumber, condition.Type, condition.Status, condition.Surfaces,
            condition.Notes, condition.RecordedAt, dentistName);
    }

    public async Task UpdateConditionAsync(Guid patientId, Guid conditionId, DentalConditionRequest request, CancellationToken ct)
    {
        var c = await db.DentalConditions.FirstOrDefaultAsync(x => x.Id == conditionId && x.PatientId == patientId, ct)
                ?? throw new NotFoundException("Condition", conditionId);
        if (c.ToothNumber != request.ToothNumber)
            throw RequestValidationException.For("toothNumber", "Tooth number cannot be changed; delete and re-add the condition.");
        c.Type = request.Type;
        c.Status = request.Status;
        c.Surfaces = request.Surfaces?.ToUpperInvariant();
        c.Notes = request.Notes?.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteConditionAsync(Guid patientId, Guid conditionId, CancellationToken ct)
    {
        var c = await db.DentalConditions.FirstOrDefaultAsync(x => x.Id == conditionId && x.PatientId == patientId, ct)
                ?? throw new NotFoundException("Condition", conditionId);
        db.DentalConditions.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetToothNoteAsync(Guid patientId, int toothNumber, ToothNoteRequest request, CancellationToken ct)
    {
        if (!Teeth.IsValid(toothNumber)) throw RequestValidationException.For("toothNumber", "Invalid FDI tooth number.");
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient", patientId);
        var tooth = await GetOrCreateToothAsync(patientId, toothNumber, ct);
        tooth.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()[..Math.Min(request.Notes.Trim().Length, 1000)];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Used when a plan item is completed: records the resulting condition (e.g. Crown) on the chart.</summary>
    public async Task RecordTreatmentResultAsync(Guid patientId, int toothNumber, ToothConditionType type, string? surfaces,
        Guid dentistId, Guid? planItemId, CancellationToken ct)
    {
        var tooth = await GetOrCreateToothAsync(patientId, toothNumber, ct);

        // Diagnosed/planned findings on this tooth are now treated.
        var open = await db.DentalConditions
            .Where(c => c.PatientId == patientId && c.ToothNumber == toothNumber
                        && (c.Status == ConditionStatus.Diagnosed || c.Status == ConditionStatus.Planned))
            .ToListAsync(ct);
        foreach (var c in open.Where(c => c.Type == type || c.Type == ToothConditionType.Caries || c.TreatmentPlanItemId == planItemId))
            c.Status = ConditionStatus.Resolved;

        db.DentalConditions.Add(new DentalCondition
        {
            PatientId = patientId,
            DentalToothId = tooth.Id,
            ToothNumber = toothNumber,
            Type = type,
            Status = ConditionStatus.Treated,
            Surfaces = surfaces,
            RecordedAt = clock.UtcNow,
            DentistId = dentistId,
            TreatmentPlanItemId = planItemId
        });
    }

    private async Task<DentalTooth> GetOrCreateToothAsync(Guid patientId, int toothNumber, CancellationToken ct)
    {
        var tooth = db.DentalTeeth.Local.FirstOrDefault(t => t.PatientId == patientId && t.ToothNumber == toothNumber)
                    ?? await db.DentalTeeth.FirstOrDefaultAsync(t => t.PatientId == patientId && t.ToothNumber == toothNumber, ct);
        if (tooth is not null) return tooth;

        tooth = new DentalTooth { PatientId = patientId, ToothNumber = toothNumber, IsPrimary = Teeth.IsPrimaryTooth(toothNumber) };
        db.DentalTeeth.Add(tooth);
        return tooth;
    }

    private async Task<Guid?> CurrentDentistIdAsync(CancellationToken ct) =>
        currentUser.UserId is null ? null
            : await db.Dentists.Where(d => d.UserId == currentUser.UserId).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);

    private static string? JoinParts(params string?[] parts)
    {
        var p = parts.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        return p.Length == 0 ? null : string.Join(" · ", p);
    }
}
