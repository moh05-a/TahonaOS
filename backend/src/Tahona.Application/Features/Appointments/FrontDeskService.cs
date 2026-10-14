using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Appointments;

/// <param name="PatientBalance">What the patient owes, or null when the caller may not see billing.</param>
/// <param name="IsLate">Still booked although its start time has passed: the patient has not been checked in.</param>
public record FrontDeskAppointmentDto(AppointmentDto Appointment, decimal? PatientBalance, bool IsLate, MessagingConsent MessagingConsent);

public record FrontDeskCountsDto(int Expected, int Arrived, int InTreatment, int Completed, int NoShow, int Cancelled);

/// <param name="Closed">Why the clinic is closed today ("holiday", "day-off"), or null on a working day.</param>
public record FrontDeskDto(DateOnly Date, IReadOnlyList<FrontDeskAppointmentDto> Appointments, FrontDeskCountsDto Counts,
    int TomorrowUnconfirmed, int FollowUpsDue, decimal? CollectedToday, string? Closed);

/// <summary>
/// What reception needs for the day on one screen: today's list in time order with each patient's balance,
/// who is late, and what is waiting (tomorrow's unconfirmed visits, follow-ups due).
/// </summary>
public class FrontDeskService(IAppDbContext db, IClinicTime clinicTime, IClock clock, ICurrentUser currentUser)
{
    private static readonly FollowUpStatus[] OpenFollowUps = [FollowUpStatus.Pending, FollowUpStatus.Contacted, FollowUpStatus.NoResponse];

    public async Task<FrontDeskDto> GetAsync(Guid? dentistId, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var start = clinicTime.StartOfDayUtc(today);
        var end = clinicTime.StartOfDayUtc(today.AddDays(1));
        var tomorrowEnd = clinicTime.StartOfDayUtc(today.AddDays(2));
        var now = clock.UtcNow;
        var seesBilling = currentUser.HasPermission(Permissions.BillingView);

        var query = db.Appointments.AsNoTracking().Where(a => a.StartTime >= start && a.StartTime < end);
        if (dentistId is not null) query = query.Where(a => a.DentistId == dentistId);
        var rows = await query.OrderBy(a => a.StartTime)
            .Select(a => new
            {
                Dto = new AppointmentDto(a.Id, a.PatientId, a.Patient.FullName, a.Patient.FullNameAr, a.Patient.Phone, a.Patient.FileNumber,
                    a.DentistId, a.Dentist.FullName, a.Dentist.Color, a.StartTime, a.EndTime, a.Treatment, a.Status, a.Notes, a.Chair, a.TreatmentPlanId,
                    a.FollowUpId != null, a.ReminderSentAt, a.Patient.Allergies != null || a.Patient.MedicalConditions != null),
                a.Patient.MessagingConsent
            })
            .ToListAsync(ct);

        var balances = new Dictionary<Guid, decimal>();
        if (seesBilling && rows.Count > 0)
        {
            var patientIds = rows.Select(r => r.Dto.PatientId).Distinct().ToList();
            balances = await db.Invoices.AsNoTracking()
                .Where(i => patientIds.Contains(i.PatientId) && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0)
                .GroupBy(i => i.PatientId).Select(g => new { g.Key, Total = g.Sum(i => i.Balance) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        }

        var appointments = rows.Select(r => new FrontDeskAppointmentDto(r.Dto,
            seesBilling ? balances.GetValueOrDefault(r.Dto.PatientId) : null,
            r.Dto.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed && r.Dto.StartTime < now.AddMinutes(-10),
            r.MessagingConsent)).ToList();

        int Count(params AppointmentStatus[] statuses) => rows.Count(r => statuses.Contains(r.Dto.Status));
        var counts = new FrontDeskCountsDto(Count(AppointmentStatus.Scheduled, AppointmentStatus.Confirmed), Count(AppointmentStatus.Arrived),
            Count(AppointmentStatus.InTreatment), Count(AppointmentStatus.Completed), Count(AppointmentStatus.NoShow), Count(AppointmentStatus.Cancelled));

        var tomorrowUnconfirmed = await db.Appointments.CountAsync(a => a.StartTime >= end && a.StartTime < tomorrowEnd && a.Status == AppointmentStatus.Scheduled, ct);
        var followUpsDue = currentUser.HasPermission(Permissions.RecoveryView)
            ? await db.FollowUps.Where(f => OpenFollowUps.Contains(f.Status) && f.DueDate <= now).Select(f => f.PatientId).Distinct().CountAsync(ct)
            : 0;

        decimal? collected = null;
        if (seesBilling)
        {
            var paid = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= start && p.PaidAt < end).Select(p => new { p.Type, p.Amount }).ToListAsync(ct);
            collected = paid.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        }

        var settings = await db.ClinicSettings.AsNoTracking().Select(s => new { s.WorkingDays, s.Holidays }).FirstAsync(ct);
        var closed = settings.Holidays.Contains(today) ? "holiday"
            : settings.WorkingDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(((int)today.DayOfWeek).ToString()) ? null : "day-off";

        return new FrontDeskDto(today, appointments, counts, tomorrowUnconfirmed, followUpsDue, collected, closed);
    }
}
