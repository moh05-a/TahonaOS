using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Dashboard;

/// <summary>Observed activity and outcomes in one period. Every figure is a count or a sum of recorded events.</summary>
/// <param name="NoShowRate">No-shows as a percentage of visits that were due (completed plus no-shows).</param>
/// <param name="FollowUpsContacted">Follow-ups on which staff (or automation) made a contact in the period.</param>
/// <param name="BookedAfterContact">Of those, how many were followed by a booking.</param>
/// <param name="PaidAfterContact">Of those, how many were followed by a payment for the same matter.</param>
/// <param name="CollectedAfterFollowUp">Payments in the period counted under the attribution rule, net of refunds.</param>
public record ImpactPeriodDto(DateOnly From, DateOnly To, int AppointmentsDue, int AppointmentsCompleted, int NoShows, double NoShowRate,
    int RemindersSent, int FollowUpsIdentified, int FollowUpsContacted, int BookedAfterContact, int PaidAfterContact,
    decimal Collected, decimal CollectedAfterFollowUp, int PlansPresented, int PlansAccepted, int PatientsSeen, int ReturningPatients);

/// <param name="Caveats">What the comparison can and cannot show.</param>
public record ImpactReportDto(ImpactPeriodDto Current, ImpactPeriodDto Previous, IReadOnlyList<string> Caveats);

/// <summary>
/// Helps an owner answer "are we following up more, and what happened after?" by putting one period beside the
/// period of equal length before it. It reports what was recorded. It does not estimate what would have happened
/// without the follow-ups, so it cannot show that the system caused a change.
/// </summary>
public class ImpactReportService(IAppDbContext db, IClinicTime clinicTime)
{
    public async Task<ImpactReportDto> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var end = to is { } t && t <= today ? t : today;
        var start = from is { } f && f <= end ? f : end.AddDays(-29);
        if (end.DayNumber - start.DayNumber > 730) start = end.AddDays(-730);
        var days = end.DayNumber - start.DayNumber + 1;

        var current = await PeriodAsync(start, end, ct);
        var previous = await PeriodAsync(start.AddDays(-days), start.AddDays(-1), ct);
        return new ImpactReportDto(current, previous,
        [
            "These are counts of recorded events in two periods of equal length. Seasons, holidays and staffing differ between periods.",
            "\"Collected after follow-up\" means a payment for the matter the clinic had contacted the patient about. It does not prove the contact caused the payment.",
            "A period before the clinic started using TahonaOS, or imported from another system, has little or no follow-up activity recorded."
        ]);
    }

    private async Task<ImpactPeriodDto> PeriodAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = clinicTime.StartOfDayUtc(from);
        var end = clinicTime.StartOfDayUtc(to.AddDays(1));

        var appointments = await db.Appointments.AsNoTracking().Where(a => a.StartTime >= start && a.StartTime < end)
            .Select(a => new { a.Status, a.PatientId }).ToListAsync(ct);
        var completed = appointments.Count(a => a.Status == AppointmentStatus.Completed);
        var noShows = appointments.Count(a => a.Status == AppointmentStatus.NoShow);
        var due = completed + noShows;

        var seen = appointments.Where(a => a.Status == AppointmentStatus.Completed).Select(a => a.PatientId).Distinct().ToList();
        var returning = seen.Count == 0 ? 0 : await db.Appointments.AsNoTracking()
            .Where(a => seen.Contains(a.PatientId) && a.Status == AppointmentStatus.Completed && a.StartTime < start)
            .Select(a => a.PatientId).Distinct().CountAsync(ct);

        var reminders = await db.Messages.CountAsync(m => m.AppointmentId != null && m.Direction == MessageDirection.Outbound
            && (m.Status == MessageStatus.Sent || m.Status == MessageStatus.Delivered || m.Status == MessageStatus.Read)
            && m.SentAt >= start && m.SentAt < end, ct);

        var identified = await db.FollowUps.CountAsync(f => f.CreatedAt >= start && f.CreatedAt < end, ct);
        var contacted = await db.FollowUps.AsNoTracking().Where(f => f.LastContactedAt >= start && f.LastContactedAt < end)
            .Select(f => new { f.Status, f.BookedAppointmentId, f.RecoveredAmount }).ToListAsync(ct);

        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= start && p.PaidAt < end)
            .Select(p => new { p.Type, p.Amount, Attributed = p.RecoveredFollowUpId != null }).ToListAsync(ct);
        decimal Signed(PaymentType type, decimal amount) => type == PaymentType.Refund ? -amount : amount;

        var presented = await db.TreatmentPlans.CountAsync(p => p.PresentedAt >= start && p.PresentedAt < end, ct);
        var accepted = await db.TreatmentPlans.CountAsync(p => p.AcceptedAt >= start && p.AcceptedAt < end, ct);

        return new ImpactPeriodDto(from, to, due, completed, noShows, due == 0 ? 0 : Math.Round(100.0 * noShows / due, 1), reminders, identified,
            contacted.Count, contacted.Count(f => f.BookedAppointmentId != null || f.Status == FollowUpStatus.Booked),
            contacted.Count(f => f.RecoveredAmount > 0), payments.Sum(p => Signed(p.Type, p.Amount)),
            payments.Where(p => p.Attributed).Sum(p => Signed(p.Type, p.Amount)), presented, accepted, seen.Count, returning);
    }
}
