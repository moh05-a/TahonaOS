using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>
/// Scans clinic data for revenue leakage. Each detector is driven by an enabled <see cref="FollowUpRule"/>:
/// the rule's DelayDays is how long a condition must persist before the patient needs action.
/// A disabled rule switches that detector off.
/// </summary>
public class RecoveryDetector(IAppDbContext db, IClock clock)
{
    private static readonly AppointmentStatus[] ActiveVisitStatuses =
        [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.Arrived, AppointmentStatus.InTreatment];

    private static readonly TreatmentPlanStatus[] OpenPlanStatuses =
        [TreatmentPlanStatus.Presented, TreatmentPlanStatus.Accepted, TreatmentPlanStatus.InProgress, TreatmentPlanStatus.PartiallyCompleted];

    public record DetectionResult(IReadOnlyList<RecoveryOpportunity> Opportunities, RecoverySettings Settings, IReadOnlyList<FollowUpRule> Rules);

    public async Task<DetectionResult> DetectAsync(CancellationToken ct, Guid? patientId = null)
    {
        var now = clock.UtcNow;
        var settings = await db.ClinicSettings.AsNoTracking().Select(s => s.Recovery).FirstAsync(ct);
        var rules = await db.FollowUpRules.AsNoTracking().Where(r => r.IsEnabled).ToListAsync(ct);
        var lookback = now.AddDays(-settings.MaxLookbackDays);

        var patients = db.Patients.AsNoTracking().Where(p => p.Status != PatientStatus.Archived);
        if (patientId is not null) patients = patients.Where(p => p.Id == patientId);
        var patientIds = patients.Select(p => p.Id);

        // Patients already coming in: they will be handled at the chair, not by outreach.
        var upcoming = (await db.Appointments.AsNoTracking()
                .Where(a => patientIds.Contains(a.PatientId) && ActiveVisitStatuses.Contains(a.Status) && a.EndTime > now.AddHours(-3))
                .Select(a => a.PatientId).Distinct().ToListAsync(ct))
            .ToHashSet();

        var results = new List<RecoveryOpportunity>();
        FollowUpRule? Rule(FollowUpTrigger t) => rules.Where(r => r.Trigger == t).OrderBy(r => r.DelayDays).FirstOrDefault();

        // ---------------------------------------------------------------- treatment plans
        var notStartedRule = Rule(FollowUpTrigger.TreatmentPlanNotStarted);
        var unfinishedRule = Rule(FollowUpTrigger.TreatmentUnfinished);
        var planOpportunityIds = new HashSet<Guid>();

        if (notStartedRule is not null || unfinishedRule is not null)
        {
            var plans = await db.TreatmentPlans.AsNoTracking()
                .Where(t => patientIds.Contains(t.PatientId) && OpenPlanStatuses.Contains(t.Status))
                .Select(t => new
                {
                    t.Id, t.PatientId, t.DentistId, t.Title, t.PlanNumber, t.Status, t.TotalAmount, t.CreatedAt,
                    t.PresentedAt, t.AcceptedAt, t.StartedAt, t.LastActivityAt,
                    ActiveSubtotal = t.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => (decimal?)i.Total) ?? 0,
                    OpenSubtotal = t.Items.Where(i => i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled)
                        .Sum(i => (decimal?)i.Total) ?? 0,
                    CompletedCount = t.Items.Count(i => i.Status == TreatmentItemStatus.Completed),
                    NextItem = t.Items.Where(i => i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled)
                        .OrderBy(i => i.Sequence).Select(i => i.Name).FirstOrDefault()
                })
                .ToListAsync(ct);

            foreach (var p in plans)
            {
                if (upcoming.Contains(p.PatientId) || p.OpenSubtotal <= 0) continue;
                // Remaining value net of the plan-level discount.
                var remaining = p.ActiveSubtotal == 0 ? 0 : Money.Round(p.OpenSubtotal * (p.TotalAmount / p.ActiveSubtotal));
                var started = p.CompletedCount > 0 || p.Status is TreatmentPlanStatus.InProgress or TreatmentPlanStatus.PartiallyCompleted;

                if (!started && notStartedRule is not null)
                {
                    var triggered = p.AcceptedAt ?? p.PresentedAt ?? p.CreatedAt;
                    var due = triggered.AddDays(notStartedRule.DelayDays);
                    if (due > now || triggered < lookback) continue;
                    results.Add(new RecoveryOpportunity(p.PatientId, RecoveryIssueType.TreatmentNotStarted, nameof(TreatmentPlan), p.Id,
                        "Treatment not started",
                        $"{p.Title} ({p.PlanNumber}) {(p.Status == TreatmentPlanStatus.Accepted ? "accepted" : "presented")} but never booked",
                        remaining, triggered, due, notStartedRule.Id, notStartedRule.Priority, p.Id, p.Title, p.DentistId));
                    planOpportunityIds.Add(p.Id);
                }
                else if (started && unfinishedRule is not null)
                {
                    var triggered = p.LastActivityAt ?? p.StartedAt ?? p.AcceptedAt ?? p.CreatedAt;
                    var due = triggered.AddDays(unfinishedRule.DelayDays);
                    if (due > now || triggered < lookback) continue;
                    results.Add(new RecoveryOpportunity(p.PatientId, RecoveryIssueType.TreatmentUnfinished, nameof(TreatmentPlan), p.Id,
                        "Treatment unfinished",
                        $"{p.Title} ({p.PlanNumber}) — next: {p.NextItem ?? "remaining items"}",
                        remaining, triggered, due, unfinishedRule.Id, unfinishedRule.Priority, p.Id, p.NextItem ?? p.Title, p.DentistId));
                    planOpportunityIds.Add(p.Id);
                }
            }
        }

        // ---------------------------------------------------------------- missed appointments
        var missedRule = Rule(FollowUpTrigger.MissedAppointment);
        if (missedRule is not null)
        {
            var since = now.AddDays(-settings.MissedAppointmentLookbackDays);
            var missed = await db.Appointments.AsNoTracking()
                .Where(a => patientIds.Contains(a.PatientId) && a.Status == AppointmentStatus.NoShow && a.StartTime >= since)
                .Where(a => !db.Appointments.Any(b => b.PatientId == a.PatientId && b.StartTime > a.StartTime
                                                      && b.Status != AppointmentStatus.NoShow && b.Status != AppointmentStatus.Cancelled))
                .Select(a => new { a.Id, a.PatientId, a.DentistId, a.StartTime, a.Treatment, a.TreatmentPlanId })
                .ToListAsync(ct);

            foreach (var a in missed.GroupBy(m => m.PatientId).Select(g => g.OrderByDescending(m => m.StartTime).First()))
            {
                if (upcoming.Contains(a.PatientId)) continue;
                var due = a.StartTime.AddDays(missedRule.DelayDays);
                if (due > now) continue;
                // If the missed visit belongs to a plan already counted, its value lives on the plan opportunity.
                var value = a.TreatmentPlanId is not null && planOpportunityIds.Contains(a.TreatmentPlanId.Value)
                    ? 0 : settings.MissedAppointmentDefaultValue;
                results.Add(new RecoveryOpportunity(a.PatientId, RecoveryIssueType.MissedAppointment, nameof(Appointment), a.Id,
                    "Missed appointment", $"{a.Treatment} on {a.StartTime:dd MMM yyyy}", value, a.StartTime, due,
                    missedRule.Id, missedRule.Priority, a.TreatmentPlanId, a.Treatment, a.DentistId));
            }
        }

        // ---------------------------------------------------------------- outstanding balances (one per patient)
        var balanceRule = Rule(FollowUpTrigger.OverdueBalance);
        if (balanceRule is not null)
        {
            var cutoff = now.AddDays(-balanceRule.DelayDays);
            var balances = await db.Invoices.AsNoTracking()
                .Where(i => patientIds.Contains(i.PatientId) && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
                            && i.Balance > 0 && i.DueDate <= cutoff)
                .GroupBy(i => i.PatientId)
                .Select(g => new { PatientId = g.Key, Total = g.Sum(i => i.Balance), Count = g.Count(), OldestDue = g.Min(i => i.DueDate) })
                .ToListAsync(ct);

            foreach (var b in balances)
            {
                results.Add(new RecoveryOpportunity(b.PatientId, RecoveryIssueType.OutstandingBalance, nameof(Patient), b.PatientId,
                    "Outstanding balance", $"{b.Count} unpaid invoice{(b.Count > 1 ? "s" : "")}, oldest due {b.OldestDue:dd MMM yyyy}",
                    b.Total, b.OldestDue, b.OldestDue.AddDays(balanceRule.DelayDays), balanceRule.Id, balanceRule.Priority));
            }
        }

        // ---------------------------------------------------------------- post-procedure clinical follow-ups
        var procedureRules = rules.Where(r => r.Trigger == FollowUpTrigger.ProcedureCompleted).ToList();
        if (procedureRules.Count > 0)
        {
            var maxDelay = procedureRules.Max(r => r.DelayDays);
            var oldest = now.AddDays(-(maxDelay + 120));
            var records = await db.TreatmentRecords.AsNoTracking()
                .Where(r => patientIds.Contains(r.PatientId) && r.PerformedAt >= oldest && r.Category != null)
                .Select(r => new { r.Id, r.PatientId, r.DentistId, r.ProcedureName, r.Category, r.PerformedAt, r.ToothNumber, r.Patient.LastVisitAt })
                .ToListAsync(ct);

            foreach (var group in records.GroupBy(r => r.PatientId))
            {
                if (upcoming.Contains(group.Key)) continue;
                var candidate = group
                    .SelectMany(r => procedureRules.Where(rule => rule.ProcedureCategory is null || rule.ProcedureCategory == r.Category)
                        .Select(rule => new { Record = r, Rule = rule, Due = r.PerformedAt.AddDays(rule.DelayDays) }))
                    // Patient has not been back since the procedure (allow same/next-day visits).
                    .Where(x => x.Due <= now && x.Due >= now.AddDays(-120)
                                && (x.Record.LastVisitAt is null || x.Record.LastVisitAt <= x.Record.PerformedAt.AddDays(1)))
                    .OrderByDescending(x => x.Record.PerformedAt)
                    .FirstOrDefault();
                if (candidate is null) continue;

                var r = candidate.Record;
                results.Add(new RecoveryOpportunity(r.PatientId, RecoveryIssueType.OverdueFollowUp, nameof(TreatmentRecord), r.Id,
                    "Overdue follow-up", $"{candidate.Rule.Name}: {r.ProcedureName}{(r.ToothNumber is null ? "" : $" (tooth {r.ToothNumber})")} on {r.PerformedAt:dd MMM yyyy}",
                    settings.FollowUpVisitValue, r.PerformedAt, candidate.Due, candidate.Rule.Id, candidate.Rule.Priority,
                    null, $"Review: {r.ProcedureName}", r.DentistId));
            }
        }

        // ---------------------------------------------------------------- routine recall
        var recallRule = Rule(FollowUpTrigger.RoutineRecall);
        if (recallRule is not null)
        {
            var recallCutoff = now.AddDays(-recallRule.DelayDays);
            var withIssues = results.Select(r => r.PatientId).ToHashSet();
            var recalls = await patients
                .Where(p => p.Status == PatientStatus.Active && p.LastVisitAt != null && p.LastVisitAt <= recallCutoff && p.LastVisitAt >= lookback)
                .Select(p => new { p.Id, p.LastVisitAt, p.PrimaryDentistId })
                .ToListAsync(ct);

            foreach (var p in recalls.Where(p => !upcoming.Contains(p.Id) && !withIssues.Contains(p.Id)))
            {
                var last = p.LastVisitAt!.Value;
                var months = (int)Math.Round((now - last).TotalDays / 30.4);
                results.Add(new RecoveryOpportunity(p.Id, RecoveryIssueType.RecallDue, nameof(Patient), p.Id,
                    "Recall due", $"Last visit {months} months ago — due for check-up & cleaning",
                    settings.RecallEstimatedValue, last, last.AddDays(recallRule.DelayDays), recallRule.Id, recallRule.Priority,
                    null, "Check-up & cleaning", p.PrimaryDentistId));
            }
        }

        return new DetectionResult(results, settings, rules);
    }
}
