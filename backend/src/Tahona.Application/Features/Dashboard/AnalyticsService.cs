using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Dashboard;

public record AnalyticsQuery(DateTimeOffset? From, DateTimeOffset? To);

public record TreatmentMetricsDto(
    int PlansCreated, int PlansPresented, int PlansAccepted, int PlansStarted, int PlansCompleted, int PlansAbandoned,
    double AcceptanceRate, double CompletionRate, decimal AverageTreatmentValue, decimal TotalPlannedValue);

public record PatientMetricsDto(int PatientsSeen, int NewPatients, int ReturningPatients, double ReturnRate, double NoShowRate,
    int Appointments, int NoShows, int Cancellations);

public record RecoveryMetricsDto(decimal RevenueRecovered, decimal PotentialLostRevenue, double RecoveryRate,
    int PatientsContacted, int PatientsRecovered, int FollowUpsCreated);

public record FinancialMetricsDto(decimal Revenue, decimal Refunds, decimal Expenses, decimal NetProfit, decimal Invoiced,
    decimal Outstanding, decimal Production);

public record NamedValueDto(string Name, decimal Value, int Count);

public record AnalyticsDto(
    DateTime From, DateTime To, string Currency,
    TreatmentMetricsDto Treatment, PatientMetricsDto Patients, RecoveryMetricsDto Recovery, FinancialMetricsDto Financial,
    IReadOnlyList<MonthlyRevenuePointDto> RevenueByMonth,
    IReadOnlyList<NamedValueDto> ProductionByDentist,
    IReadOnlyList<NamedValueDto> ProductionByCategory,
    IReadOnlyList<NamedValueDto> TopProcedures,
    IReadOnlyList<NamedValueDto> PaymentMethods,
    IReadOnlyList<NamedValueDto> ExpensesByCategory,
    IReadOnlyList<NamedValueDto> RecoveryByIssue,
    IReadOnlyList<FunnelStepDto> Funnel);

public class AnalyticsService(
    IAppDbContext db,
    DashboardService dashboard,
    RecoveryService recovery,
    IClinicTime clinicTime,
    IClock clock)
{
    public async Task<AnalyticsDto> GetAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var to = query.To?.UtcDateTime ?? now;
        var from = query.From?.UtcDateTime ?? clinicTime.StartOfMonthUtc(clinicTime.Today.AddMonths(-5));
        if (from >= to) throw RequestValidationException.For("from", "Start date must be before end date.");
        if ((to - from).TotalDays > 1100) throw RequestValidationException.For("from", "Range cannot exceed 3 years.");
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);

        // ---------------------------------------------------------------- treatment
        var plans = await db.TreatmentPlans.AsNoTracking().Where(t => t.CreatedAt >= from && t.CreatedAt < to)
            .Select(t => new { t.Status, t.TotalAmount, t.PresentedAt, t.AcceptedAt, t.StartedAt, t.RejectedAt }).ToListAsync(ct);
        var nonDraft = plans.Where(p => p.Status != TreatmentPlanStatus.Draft).ToList();
        var created = plans.Count;
        var accepted = plans.Count(p => p.AcceptedAt != null);
        var completed = plans.Count(p => p.Status == TreatmentPlanStatus.Completed);
        var abandoned = plans.Count(p => p.Status == TreatmentPlanStatus.Rejected && p.StartedAt != null
                                         || p.Status == TreatmentPlanStatus.PartiallyCompleted);
        var treatment = new TreatmentMetricsDto(
            created, nonDraft.Count, accepted, plans.Count(p => p.StartedAt != null), completed, abandoned,
            Pct(accepted, created), Pct(completed, created),
            nonDraft.Count == 0 ? 0 : Money.Round(nonDraft.Average(p => p.TotalAmount)), nonDraft.Sum(p => p.TotalAmount));

        // ---------------------------------------------------------------- patients
        var appts = await db.Appointments.AsNoTracking().Where(a => a.StartTime >= from && a.StartTime < to && a.StartTime < now)
            .Select(a => new { a.PatientId, a.Status, a.StartTime }).ToListAsync(ct);
        var seen = appts.Where(a => a.Status == AppointmentStatus.Completed).Select(a => a.PatientId).Distinct().ToList();
        var priorVisitors = (await db.Appointments.AsNoTracking()
                .Where(a => a.Status == AppointmentStatus.Completed && a.StartTime < from && seen.Contains(a.PatientId))
                .Select(a => a.PatientId).Distinct().ToListAsync(ct)).ToHashSet();
        var returning = seen.Count(priorVisitors.Contains);
        var noShows = appts.Count(a => a.Status == AppointmentStatus.NoShow);
        var completedAppts = appts.Count(a => a.Status == AppointmentStatus.Completed);
        var newPatients = await db.Patients.CountAsync(p => p.RegisteredAt >= from && p.RegisteredAt < to, ct);
        var patients = new PatientMetricsDto(seen.Count, newPatients, returning, Pct(returning, seen.Count),
            Pct(noShows, noShows + completedAppts), appts.Count, noShows, appts.Count(a => a.Status == AppointmentStatus.Cancelled));

        // ---------------------------------------------------------------- financial
        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= from && p.PaidAt < to)
            .Select(p => new { p.Type, p.Amount, p.Method, p.PatientId, Recovered = p.RecoveredFollowUpId != null, IssueType = (RecoveryIssueType?)p.RecoveredFollowUp!.IssueType })
            .ToListAsync(ct);
        var revenue = payments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
        var refunds = payments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= from && e.ExpenseDate < to)
            .Select(e => new { e.Category, e.Amount }).ToListAsync(ct);
        var invoiced = await db.Invoices.Where(i => i.IssueDate >= from && i.IssueDate < to && i.Status != InvoiceStatus.Void && i.Status != InvoiceStatus.Draft)
            .SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var outstanding = await db.Invoices.Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid))
            .SumAsync(i => (decimal?)i.Balance, ct) ?? 0;

        var records = await db.TreatmentRecords.AsNoTracking().Where(r => r.PerformedAt >= from && r.PerformedAt < to)
            .Select(r => new { r.Price, r.ProcedureName, r.Category, Dentist = r.Dentist.FullName }).ToListAsync(ct);
        var financial = new FinancialMetricsDto(revenue - refunds, refunds, expenses.Sum(e => e.Amount),
            revenue - refunds - expenses.Sum(e => e.Amount), invoiced, outstanding, records.Sum(r => r.Price));

        // ---------------------------------------------------------------- recovery
        var recovered = payments.Where(p => p.Recovered).Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        var (current, _) = await recovery.BuildPatientsAsync(null, ct);
        var followUps = await db.FollowUps.AsNoTracking()
            .Where(f => f.CreatedAt >= from && f.CreatedAt < to || f.LastContactedAt >= from && f.LastContactedAt < to)
            .Select(f => new { f.Status, f.LastContactedAt, f.CreatedAt, f.PatientId }).ToListAsync(ct);
        var contacted = followUps.Where(f => f.LastContactedAt >= from && f.LastContactedAt < to).ToList();
        var recoveryMetrics = new RecoveryMetricsDto(
            recovered, current.Sum(p => p.TotalValue),
            Pct(contacted.Count(f => f.Status is FollowUpStatus.Booked or FollowUpStatus.Recovered), contacted.Count),
            contacted.Select(f => f.PatientId).Distinct().Count(),
            payments.Where(p => p.Recovered && p.Type == PaymentType.Payment).Select(p => p.PatientId).Distinct().Count(),
            followUps.Count(f => f.CreatedAt >= from && f.CreatedAt < to));

        var months = Math.Clamp((int)Math.Ceiling((to - from).TotalDays / 30.4), 1, 36);

        return new AnalyticsDto(from, to, currency, treatment, patients, recoveryMetrics, financial,
            await dashboard.RevenueChartAsync(months, ct),
            records.GroupBy(r => r.Dentist).Select(g => new NamedValueDto(g.Key, g.Sum(r => r.Price), g.Count()))
                .OrderByDescending(x => x.Value).ToList(),
            records.GroupBy(r => r.Category?.ToString() ?? "Other").Select(g => new NamedValueDto(g.Key, g.Sum(r => r.Price), g.Count()))
                .OrderByDescending(x => x.Value).ToList(),
            records.GroupBy(r => r.ProcedureName).Select(g => new NamedValueDto(g.Key, g.Sum(r => r.Price), g.Count()))
                .OrderByDescending(x => x.Value).Take(10).ToList(),
            payments.Where(p => p.Type == PaymentType.Payment).GroupBy(p => p.Method)
                .Select(g => new NamedValueDto(g.Key.ToString(), g.Sum(p => p.Amount), g.Count())).OrderByDescending(x => x.Value).ToList(),
            expenses.GroupBy(e => e.Category).Select(g => new NamedValueDto(g.Key.ToString(), g.Sum(e => e.Amount), g.Count()))
                .OrderByDescending(x => x.Value).ToList(),
            payments.Where(p => p.Recovered && p.IssueType != null).GroupBy(p => p.IssueType!.Value)
                .Select(g => new NamedValueDto(g.Key.ToString(), g.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount), g.Count()))
                .OrderByDescending(x => x.Value).ToList(),
            await dashboard.FunnelAsync(from, to, ct));
    }

    private static double Pct(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);
}
