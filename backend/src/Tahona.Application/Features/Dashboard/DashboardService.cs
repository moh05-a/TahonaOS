using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Appointments;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Dashboard;

public record KpiDto(decimal Value, decimal? Previous);

public record RecoveryCardDto(decimal PotentialRevenue, decimal RecoveredThisMonth, int PatientsRequiringFollowUp, int HighPriority,
    IReadOnlyList<RecoveryPatientDto> TopPatients);

public record MonthlyRevenuePointDto(string Month, decimal Revenue, decimal Expenses, decimal Recovered);

public record WeeklyAppointmentsPointDto(string WeekStart, int Completed, int NoShow, int Cancelled, int Upcoming);

public record FunnelStepDto(string Step, int Count, decimal Value);

public record RetentionPointDto(string Month, int NewPatients, int ReturningPatients);

public record DashboardDto(
    int TodayAppointments,
    int TodayCompleted,
    int TodayRemaining,
    decimal TodayRevenue,
    decimal OutstandingPayments,
    int OverdueInvoices,
    decimal TreatmentRevenuePotential,
    int OpenTreatmentPlans,
    double NoShowRate30d,
    int NewPatientsThisMonth,
    RecoveryCardDto Recovery,
    IReadOnlyList<AppointmentDto> TodaySchedule,
    IReadOnlyList<MonthlyRevenuePointDto> RevenueChart,
    IReadOnlyList<WeeklyAppointmentsPointDto> AppointmentsChart,
    IReadOnlyList<FunnelStepDto> TreatmentFunnel,
    IReadOnlyList<RetentionPointDto> RetentionChart,
    IReadOnlyList<RecoveryTrendPointDto> RecoveryChart,
    string Currency);

public class DashboardService(
    IAppDbContext db,
    AppointmentService appointments,
    RecoveryService recovery,
    IClinicTime clinicTime,
    IClock clock)
{
    private static readonly TreatmentPlanStatus[] OpenPlanStatuses =
        [TreatmentPlanStatus.Presented, TreatmentPlanStatus.Accepted, TreatmentPlanStatus.InProgress, TreatmentPlanStatus.PartiallyCompleted];

    public async Task<DashboardDto> GetAsync(Guid? dentistId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = clinicTime.Today;
        var dayStart = clinicTime.StartOfDayUtc(today);
        var dayEnd = clinicTime.StartOfDayUtc(today.AddDays(1));
        var monthStart = clinicTime.StartOfMonthUtc(today);
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);

        var schedule = await appointments.ListAsync(new AppointmentQuery(dayStart, dayEnd, dentistId, null, null, 500), ct);
        var activeToday = schedule.Where(a => a.Status != AppointmentStatus.Cancelled).ToList();

        var todayRevenue = await db.Payments.Where(p => p.PaidAt >= dayStart && p.PaidAt < dayEnd)
            .SumAsync(p => (decimal?)(p.Type == PaymentType.Refund ? -p.Amount : p.Amount), ct) ?? 0;

        var open = db.Invoices.Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0);
        var outstanding = await open.SumAsync(i => (decimal?)i.Balance, ct) ?? 0;
        var overdueCount = await open.CountAsync(i => i.DueDate < now, ct);

        var potential = await db.TreatmentPlanItems
            .Where(i => OpenPlanStatuses.Contains(i.TreatmentPlan.Status)
                        && (i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled))
            .SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var openPlans = await db.TreatmentPlans.CountAsync(t => OpenPlanStatuses.Contains(t.Status), ct);

        var since30 = now.AddDays(-30);
        var recentAppts = await db.Appointments.Where(a => a.StartTime >= since30 && a.StartTime < now)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                NoShow = g.Count(a => a.Status == AppointmentStatus.NoShow)
            }).FirstOrDefaultAsync(ct);
        var attended = (recentAppts?.Completed ?? 0) + (recentAppts?.NoShow ?? 0);
        var noShowRate = attended == 0 ? 0 : Math.Round(100.0 * (recentAppts!.NoShow) / attended, 1);

        var newPatients = await db.Patients.CountAsync(p => p.RegisteredAt >= monthStart, ct);

        var (recoveryPatients, settings) = await recovery.BuildPatientsAsync(null, ct);
        var recoveredThisMonth = await db.Payments.Where(p => p.RecoveredFollowUpId != null && p.PaidAt >= monthStart)
            .SumAsync(p => (decimal?)(p.Type == PaymentType.Refund ? -p.Amount : p.Amount), ct) ?? 0;
        var recoveryCard = new RecoveryCardDto(
            recoveryPatients.Sum(p => p.TotalValue), recoveredThisMonth, recoveryPatients.Count(p => p.IsDue),
            recoveryPatients.Count(p => p.IsDue && p.Score.Priority >= PriorityLevel.High),
            recoveryPatients.Where(p => p.IsDue).Take(5).ToList());

        return new DashboardDto(
            activeToday.Count,
            activeToday.Count(a => a.Status == AppointmentStatus.Completed),
            activeToday.Count(a => a.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed or AppointmentStatus.Arrived or AppointmentStatus.InTreatment),
            todayRevenue, outstanding, overdueCount, potential, openPlans, noShowRate, newPatients, recoveryCard,
            schedule,
            await RevenueChartAsync(12, ct),
            await AppointmentsChartAsync(8, ct),
            await FunnelAsync(clinicTime.StartOfMonthUtc(today.AddMonths(-5)), now, ct),
            await RetentionAsync(6, ct),
            await recovery.TrendAsync(6, ct),
            currency);
    }

    public async Task<IReadOnlyList<MonthlyRevenuePointDto>> RevenueChartAsync(int months, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var start = clinicTime.StartOfMonthUtc(today.AddMonths(-(months - 1)));
        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= start)
            .Select(p => new { p.PaidAt, Signed = p.Type == PaymentType.Refund ? -p.Amount : p.Amount, Recovered = p.RecoveredFollowUpId != null })
            .ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= start)
            .Select(e => new { e.ExpenseDate, e.Amount }).ToListAsync(ct);

        var points = new List<MonthlyRevenuePointDto>();
        for (var i = months - 1; i >= 0; i--)
        {
            var month = today.AddMonths(-i);
            var from = clinicTime.StartOfMonthUtc(month);
            var to = clinicTime.StartOfMonthUtc(month.AddMonths(1));
            points.Add(new MonthlyRevenuePointDto(
                new DateOnly(month.Year, month.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                payments.Where(p => p.PaidAt >= from && p.PaidAt < to).Sum(p => p.Signed),
                expenses.Where(e => e.ExpenseDate >= from && e.ExpenseDate < to).Sum(e => e.Amount),
                payments.Where(p => p.Recovered && p.PaidAt >= from && p.PaidAt < to).Sum(p => p.Signed)));
        }
        return points;
    }

    public async Task<IReadOnlyList<WeeklyAppointmentsPointDto>> AppointmentsChartAsync(int weeks, CancellationToken ct)
    {
        var today = clinicTime.Today;
        // Jordan's working week starts on Saturday.
        var offset = ((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var thisWeek = today.AddDays(-offset);
        var firstWeek = thisWeek.AddDays(-7 * (weeks - 2)); // include next week
        var start = clinicTime.StartOfDayUtc(firstWeek.AddDays(-7));
        var end = clinicTime.StartOfDayUtc(thisWeek.AddDays(14));

        var rows = await db.Appointments.AsNoTracking().Where(a => a.StartTime >= start && a.StartTime < end)
            .Select(a => new { a.StartTime, a.Status }).ToListAsync(ct);

        var points = new List<WeeklyAppointmentsPointDto>();
        for (var w = 0; w < weeks; w++)
        {
            var weekStart = thisWeek.AddDays(-7 * (weeks - 2 - w));
            var from = clinicTime.StartOfDayUtc(weekStart);
            var to = clinicTime.StartOfDayUtc(weekStart.AddDays(7));
            var inWeek = rows.Where(r => r.StartTime >= from && r.StartTime < to).ToList();
            points.Add(new WeeklyAppointmentsPointDto(weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                inWeek.Count(r => r.Status == AppointmentStatus.Completed),
                inWeek.Count(r => r.Status == AppointmentStatus.NoShow),
                inWeek.Count(r => r.Status == AppointmentStatus.Cancelled),
                inWeek.Count(r => r.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed or AppointmentStatus.Arrived or AppointmentStatus.InTreatment)));
        }
        return points;
    }

    public async Task<IReadOnlyList<FunnelStepDto>> FunnelAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var plans = await db.TreatmentPlans.AsNoTracking().Where(t => t.CreatedAt >= from && t.CreatedAt <= to)
            .Select(t => new { t.Status, t.TotalAmount, t.PresentedAt, t.AcceptedAt, t.StartedAt }).ToListAsync(ct);

        var presented = plans.Where(p => p.PresentedAt != null || p.Status != TreatmentPlanStatus.Draft).ToList();
        var accepted = plans.Where(p => p.AcceptedAt != null).ToList();
        var started = plans.Where(p => p.StartedAt != null).ToList();
        var completed = plans.Where(p => p.Status == TreatmentPlanStatus.Completed).ToList();
        return
        [
            new("Presented", presented.Count, presented.Sum(p => p.TotalAmount)),
            new("Accepted", accepted.Count, accepted.Sum(p => p.TotalAmount)),
            new("Started", started.Count, started.Sum(p => p.TotalAmount)),
            new("Completed", completed.Count, completed.Sum(p => p.TotalAmount))
        ];
    }

    public async Task<IReadOnlyList<RetentionPointDto>> RetentionAsync(int months, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var start = clinicTime.StartOfMonthUtc(today.AddMonths(-(months - 1)));
        var visits = await db.Appointments.AsNoTracking().Where(a => a.Status == AppointmentStatus.Completed && a.StartTime >= start)
            .Select(a => new { a.PatientId, a.StartTime }).ToListAsync(ct);
        var ids = visits.Select(v => v.PatientId).Distinct().ToList();
        var firstVisit = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.Completed && ids.Contains(a.PatientId))
            .GroupBy(a => a.PatientId).Select(g => new { g.Key, First = g.Min(a => a.StartTime) })
            .ToDictionaryAsync(x => x.Key, x => x.First, ct);

        var points = new List<RetentionPointDto>();
        for (var i = months - 1; i >= 0; i--)
        {
            var month = today.AddMonths(-i);
            var from = clinicTime.StartOfMonthUtc(month);
            var to = clinicTime.StartOfMonthUtc(month.AddMonths(1));
            var seen = visits.Where(v => v.StartTime >= from && v.StartTime < to).Select(v => v.PatientId).Distinct().ToList();
            var newCount = seen.Count(id => firstVisit[id] >= from);
            points.Add(new RetentionPointDto(new DateOnly(month.Year, month.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                newCount, seen.Count - newCount));
        }
        return points;
    }
}
