using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Appointments;

public record AppointmentQuery(
    DateTimeOffset? From, DateTimeOffset? To, Guid? DentistId, Guid? PatientId, AppointmentStatus? Status, int? Take);

public record AppointmentDto(
    Guid Id, Guid PatientId, string PatientName, string? PatientNameAr, string PatientPhone, string PatientFileNumber,
    Guid DentistId, string DentistName, string DentistColor, DateTime StartTime, DateTime EndTime,
    string Treatment, AppointmentStatus Status, string? Notes, string? Chair, Guid? TreatmentPlanId,
    bool FromRecovery, DateTime? ReminderSentAt, bool HasMedicalAlert)
{
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
}

public record AppointmentUpsertRequest(
    Guid PatientId, Guid DentistId, DateTimeOffset StartTime, int DurationMinutes, string Treatment,
    string? Notes, string? Chair, Guid? TreatmentPlanId, Guid? FollowUpId, bool AllowOverlap = false);

public record RescheduleRequest(DateTimeOffset StartTime, DateTimeOffset EndTime, Guid? DentistId, bool AllowOverlap = false);

public record AppointmentStatusRequest(AppointmentStatus Status, string? Reason);

public class AppointmentUpsertValidator : AbstractValidator<AppointmentUpsertRequest>
{
    public AppointmentUpsertValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DentistId).NotEmpty();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 480);
        RuleFor(x => x.Treatment).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Chair).MaximumLength(30);
    }
}

public class RescheduleValidator : AbstractValidator<RescheduleRequest>
{
    public RescheduleValidator()
    {
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.");
        RuleFor(x => x).Must(x => (x.EndTime - x.StartTime).TotalMinutes <= 480).WithMessage("Appointments cannot exceed 8 hours.");
    }
}

public class AppointmentService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    NotificationService notifications)
{
    private static readonly AppointmentStatus[] BlockingStatuses =
        [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.Arrived, AppointmentStatus.InTreatment, AppointmentStatus.Completed];

    private static readonly FollowUpStatus[] OpenFollowUpStatuses =
        [FollowUpStatus.Pending, FollowUpStatus.Contacted, FollowUpStatus.NoResponse];

    public async Task<IReadOnlyList<AppointmentDto>> ListAsync(AppointmentQuery query, CancellationToken ct)
    {
        var q = db.Appointments.AsNoTracking();
        if (query.From is not null) q = q.Where(a => a.EndTime >= query.From.Value.UtcDateTime);
        if (query.To is not null) q = q.Where(a => a.StartTime < query.To.Value.UtcDateTime);
        if (query.DentistId is not null) q = q.Where(a => a.DentistId == query.DentistId);
        if (query.PatientId is not null) q = q.Where(a => a.PatientId == query.PatientId);
        if (query.Status is not null) q = q.Where(a => a.Status == query.Status);

        // Bound unfiltered range queries to keep the calendar fast.
        var ordered = query.PatientId is not null ? q.OrderByDescending(a => a.StartTime) : q.OrderBy(a => a.StartTime);
        return await Project(ordered.Take(Math.Clamp(query.Take ?? 1000, 1, 2000))).ToListAsync(ct);
    }

    public async Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Appointments.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Appointment", id);

    public async Task<AppointmentDto> CreateAsync(AppointmentUpsertRequest request, CancellationToken ct)
    {
        var start = request.StartTime.UtcDateTime;
        var end = start.AddMinutes(request.DurationMinutes);

        // Check-then-insert runs under a lock per dentist and per patient, so two simultaneous bookings cannot both pass.
        var (appt, patientName, dentistUserId) = await db.WithLocksAsync(BookingLocks(request.DentistId, request.PatientId), async () =>
        {
            var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, ct)
                          ?? throw RequestValidationException.For("patientId", "Patient not found.");
            var dentist = await db.Dentists.FirstOrDefaultAsync(d => d.Id == request.DentistId && d.IsActive, ct)
                          ?? throw RequestValidationException.For("dentistId", "Dentist not found or inactive.");
            await EnsurePlanAsync(request.TreatmentPlanId, request.PatientId, ct);
            if (!request.AllowOverlap) await EnsureNoOverlapAsync(dentist.Id, patient.Id, start, end, null, ct);

            var created = new Appointment
            {
                PatientId = patient.Id,
                DentistId = dentist.Id,
                StartTime = start,
                EndTime = end,
                Treatment = request.Treatment.Trim(),
                Notes = request.Notes?.Trim(),
                Chair = request.Chair?.Trim(),
                TreatmentPlanId = request.TreatmentPlanId
            };
            db.Appointments.Add(created);

            // Recovery attribution: a booking for a patient with open follow-ups closes them as "Booked".
            if (start > clock.UtcNow) await AttributeBookingAsync(created, request.FollowUpId, ct);

            if (patient.Status == PatientStatus.Inactive) patient.Status = PatientStatus.Active;
            await db.SaveChangesAsync(ct);
            return (created, patient.FullName, dentist.UserId);
        }, ct);

        if (dentistUserId is not null)
        {
            await notifications.NotifyAsync(new NotifyRequest(
                NotificationType.NewAppointment, PriorityLevel.Low,
                $"New appointment: {patientName}",
                $"{appt.Treatment} · {start:yyyy-MM-dd HH:mm} UTC",
                $"/appointments?date={start:yyyy-MM-dd}", nameof(Appointment), appt.Id,
                UserIds: [dentistUserId.Value], ExcludeUserId: currentUser.UserId), ct);
        }

        return await GetAsync(appt.Id, ct);
    }

    public async Task<AppointmentDto> UpdateAsync(Guid id, AppointmentUpsertRequest request, CancellationToken ct)
    {
        var patientId = await db.Appointments.Where(a => a.Id == id).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Appointment", id);
        var start = request.StartTime.UtcDateTime;
        var end = start.AddMinutes(request.DurationMinutes);

        await db.WithLocksAsync(BookingLocks(request.DentistId, patientId), async () =>
        {
            var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Appointment", id);
            if (appt.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled)
                throw new BusinessRuleException("Completed or cancelled appointments cannot be edited.");

            if (!await db.Dentists.AnyAsync(d => d.Id == request.DentistId && d.IsActive, ct))
                throw RequestValidationException.For("dentistId", "Dentist not found or inactive.");
            await EnsurePlanAsync(request.TreatmentPlanId, appt.PatientId, ct);
            if (!request.AllowOverlap) await EnsureNoOverlapAsync(request.DentistId, appt.PatientId, start, end, id, ct);

            if (appt.StartTime != start) ResetReminder(appt);
            appt.DentistId = request.DentistId;
            appt.StartTime = start;
            appt.EndTime = end;
            appt.Treatment = request.Treatment.Trim();
            appt.Notes = request.Notes?.Trim();
            appt.Chair = request.Chair?.Trim();
            appt.TreatmentPlanId = request.TreatmentPlanId;
            if (appt.Status == AppointmentStatus.NoShow && start > clock.UtcNow) appt.Status = AppointmentStatus.Scheduled;
            return await db.SaveChangesAsync(ct);
        }, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Drag-and-drop / resize from the calendar.</summary>
    public async Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct)
    {
        var current = await db.Appointments.Where(a => a.Id == id).Select(a => new { a.PatientId, a.DentistId }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Appointment", id);
        var dentistId = request.DentistId ?? current.DentistId;
        var start = request.StartTime.UtcDateTime;
        var end = request.EndTime.UtcDateTime;

        await db.WithLocksAsync(BookingLocks(dentistId, current.PatientId), async () =>
        {
            var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Appointment", id);
            if (appt.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.InTreatment)
                throw new BusinessRuleException($"A {appt.Status} appointment cannot be moved.");

            if (dentistId != appt.DentistId && !await db.Dentists.AnyAsync(d => d.Id == dentistId && d.IsActive, ct))
                throw RequestValidationException.For("dentistId", "Dentist not found or inactive.");
            if (!request.AllowOverlap) await EnsureNoOverlapAsync(dentistId, appt.PatientId, start, end, id, ct);

            appt.StartTime = start;
            appt.EndTime = end;
            appt.DentistId = dentistId;
            ResetReminder(appt);
            if (appt.Status == AppointmentStatus.NoShow && start > clock.UtcNow) appt.Status = AppointmentStatus.Scheduled;
            if (appt.Status == AppointmentStatus.Confirmed) appt.Status = AppointmentStatus.Scheduled; // needs re-confirmation
            return await db.SaveChangesAsync(ct);
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<AppointmentDto> ChangeStatusAsync(Guid id, AppointmentStatusRequest request, CancellationToken ct)
    {
        var appt = await db.Appointments.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Id == id, ct)
                   ?? throw new NotFoundException("Appointment", id);
        var now = clock.UtcNow;

        if (appt.Status == request.Status) return await GetAsync(id, ct);
        if (appt.Status == AppointmentStatus.Completed && request.Status != AppointmentStatus.InTreatment)
            throw new BusinessRuleException("A completed appointment cannot change status.");
        if (request.Status is AppointmentStatus.Completed or AppointmentStatus.Arrived or AppointmentStatus.InTreatment
            && appt.StartTime > now.AddHours(12))
            throw new BusinessRuleException("You cannot check in or complete an appointment that is in the future.");
        if (request.Status == AppointmentStatus.NoShow && appt.StartTime > now)
            throw new BusinessRuleException("An appointment can only be marked as no-show after its start time.");

        appt.Status = request.Status;
        switch (request.Status)
        {
            case AppointmentStatus.Confirmed: appt.ConfirmedAt = now; break;
            case AppointmentStatus.Arrived: appt.ArrivedAt = now; break;
            case AppointmentStatus.InTreatment: appt.ArrivedAt ??= now; break;
            case AppointmentStatus.Completed:
                appt.CompletedAt = now;
                if (appt.Patient.LastVisitAt is null || appt.Patient.LastVisitAt < appt.StartTime)
                    appt.Patient.LastVisitAt = appt.StartTime;
                if (appt.TreatmentPlanId is not null)
                {
                    var plan = await db.TreatmentPlans.FirstAsync(t => t.Id == appt.TreatmentPlanId, ct);
                    plan.LastActivityAt = now;
                    if (plan.Status is TreatmentPlanStatus.Presented or TreatmentPlanStatus.Accepted)
                    {
                        plan.Status = TreatmentPlanStatus.InProgress;
                        plan.AcceptedAt ??= now;
                        plan.StartedAt ??= now;
                    }
                }
                break;
            case AppointmentStatus.Cancelled:
                appt.CancelledAt = now;
                appt.CancellationReason = request.Reason?.Trim();
                break;
        }
        await db.SaveChangesAsync(ct);

        if (request.Status == AppointmentStatus.NoShow)
        {
            await notifications.NotifyAsync(new NotifyRequest(
                NotificationType.MissedAppointment, PriorityLevel.High,
                $"Missed appointment: {appt.Patient.FullName}",
                $"{appt.Treatment} — added to Revenue Recovery for follow-up.",
                $"/patients/{appt.PatientId}", nameof(Appointment), appt.Id,
                RequiredPermission: Permissions.RecoveryManage), ct);
        }

        return await GetAsync(id, ct);
    }

    private async Task AttributeBookingAsync(Appointment appt, Guid? followUpId, CancellationToken ct)
    {
        var open = await db.FollowUps
            .Where(f => f.PatientId == appt.PatientId && OpenFollowUpStatuses.Contains(f.Status)
                        && f.IssueType != RecoveryIssueType.OutstandingBalance)
            .ToListAsync(ct);

        foreach (var f in open)
        {
            f.Status = FollowUpStatus.Booked;
            f.BookedAppointmentId = appt.Id;
            f.Outcome ??= "Appointment booked";
        }

        appt.FollowUpId = followUpId is not null && open.Any(f => f.Id == followUpId)
            ? followUpId
            : open.Where(f => f.LastContactedAt != null).OrderByDescending(f => f.LastContactedAt).Select(f => (Guid?)f.Id).FirstOrDefault();
    }

    /// <summary>One lock per dentist diary and one per patient; the tenant filter keeps ids unique to the clinic.</summary>
    private static string[] BookingLocks(Guid dentistId, Guid patientId) => [$"booking:dentist:{dentistId}", $"booking:patient:{patientId}"];

    /// <summary>A moved appointment needs a fresh reminder.</summary>
    private static void ResetReminder(Appointment appt)
    {
        appt.ReminderSentAt = null;
        appt.ReminderAttempts = 0;
    }

    private async Task EnsurePlanAsync(Guid? planId, Guid patientId, CancellationToken ct)
    {
        if (planId is not null && !await db.TreatmentPlans.AnyAsync(t => t.Id == planId && t.PatientId == patientId, ct))
            throw RequestValidationException.For("treatmentPlanId", "Treatment plan not found for this patient.");
    }

    private async Task EnsureNoOverlapAsync(Guid dentistId, Guid patientId, DateTime start, DateTime end, Guid? excludeId, CancellationToken ct)
    {
        var clash = await db.Appointments.AsNoTracking()
            .Where(a => a.Id != excludeId && BlockingStatuses.Contains(a.Status) && a.StartTime < end && a.EndTime > start
                        && (a.DentistId == dentistId || a.PatientId == patientId))
            .Select(a => new { a.DentistId, a.StartTime, Patient = a.Patient.FullName, Dentist = a.Dentist.FullName })
            .FirstOrDefaultAsync(ct);

        if (clash is null) return;
        throw clash.DentistId == dentistId
            ? new ConflictException($"{clash.Dentist} already has an appointment with {clash.Patient} at that time.")
            : new ConflictException($"{clash.Patient} already has another appointment at that time.");
    }

    private static IQueryable<AppointmentDto> Project(IQueryable<Appointment> q) => q.Select(a => new AppointmentDto(
        a.Id, a.PatientId, a.Patient.FullName, a.Patient.FullNameAr, a.Patient.Phone, a.Patient.FileNumber,
        a.DentistId, a.Dentist.FullName, a.Dentist.Color, a.StartTime, a.EndTime, a.Treatment, a.Status, a.Notes, a.Chair, a.TreatmentPlanId,
        a.FollowUpId != null, a.ReminderSentAt,
        a.Patient.Allergies != null || a.Patient.MedicalConditions != null));
}
