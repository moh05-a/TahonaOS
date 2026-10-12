using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Messaging;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

public record RecoveryQuery(PriorityLevel? Priority, RecoveryIssueType? IssueType, bool? DueOnly, string? Stage, int? Take);

public record ContactPatientRequest(MessageChannel Channel, Guid? TemplateId, MessageTemplateType? TemplateType, string? Body, string? Notes);

public record ContactPatientResult(Guid? MessageId, MessageStatus? MessageStatus, string? ManualSendUrl, string? Body, int FollowUpsUpdated);

public enum ContactOutcome { NoResponse = 1, CallBackLater = 2, Declined = 3, AlreadyHandled = 4 }

public record OutcomeRequest(ContactOutcome Outcome, string? Notes, IReadOnlyList<RecoveryIssueType>? IssueTypes);

public class ContactPatientRequestValidator : AbstractValidator<ContactPatientRequest>
{
    public ContactPatientRequestValidator()
    {
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Body).MaximumLength(4000);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class OutcomeRequestValidator : AbstractValidator<OutcomeRequest>
{
    public OutcomeRequestValidator()
    {
        RuleFor(x => x.Outcome).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class RecoveryService(
    IAppDbContext db,
    RecoveryDetector detector,
    MessagingService messaging,
    ICurrentUser currentUser,
    IClinicTime clinicTime,
    IClock clock)
{
    private static readonly FollowUpStatus[] OpenStatuses = [FollowUpStatus.Pending, FollowUpStatus.Contacted, FollowUpStatus.NoResponse];
    private static readonly RecoveryIssueType[] PlanIssues = [RecoveryIssueType.TreatmentNotStarted, RecoveryIssueType.TreatmentUnfinished];

    // ================================================================== read model

    public async Task<RecoveryDashboardDto> GetDashboardAsync(RecoveryQuery query, CancellationToken ct)
    {
        var (patients, settings) = await BuildPatientsAsync(null, ct);
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);
        var now = clock.UtcNow;

        var today = clinicTime.Today;
        var monthStart = clinicTime.StartOfMonthUtc(today);
        var lastMonthStart = clinicTime.StartOfMonthUtc(today.AddMonths(-1));
        var recovered = await RecoveredBetweenAsync(lastMonthStart, now, ct);
        var recoveredThisMonth = recovered.Where(r => r.PaidAt >= monthStart).Sum(r => r.Signed);
        var recoveredLastMonth = recovered.Where(r => r.PaidAt < monthStart).Sum(r => r.Signed);

        var issues = patients.SelectMany(p => p.Issues).ToList();
        var since90 = now.AddDays(-90);
        var contacted = await db.FollowUps.AsNoTracking()
            .Where(f => f.LastContactedAt >= since90)
            .Select(f => f.Status).ToListAsync(ct);
        var rate = contacted.Count == 0 ? 0
            : Math.Round(100.0 * contacted.Count(s => s is FollowUpStatus.Booked or FollowUpStatus.Recovered) / contacted.Count, 1);

        var summary = new RecoverySummaryDto(
            PotentialRevenue: patients.Sum(p => p.TotalValue),
            RecoveredThisMonth: recoveredThisMonth,
            RecoveredLastMonth: recoveredLastMonth,
            PatientsToContact: patients.Count(p => p.IsDue),
            PatientsInProgress: patients.Count(p => !p.IsDue),
            HighValuePatients: patients.Count(p => p.TotalValue >= settings.HighValueThreshold),
            MissedAppointments: issues.Count(i => i.IssueType == RecoveryIssueType.MissedAppointment),
            UnfinishedTreatments: issues.Count(i => i.IssueType == RecoveryIssueType.TreatmentUnfinished),
            NotStartedTreatments: issues.Count(i => i.IssueType == RecoveryIssueType.TreatmentNotStarted),
            OutstandingBalanceCount: issues.Count(i => i.IssueType == RecoveryIssueType.OutstandingBalance),
            OutstandingBalanceTotal: issues.Where(i => i.IssueType == RecoveryIssueType.OutstandingBalance).Sum(i => i.Value),
            OverdueFollowUps: issues.Count(i => i.IssueType == RecoveryIssueType.OverdueFollowUp),
            RecallsDue: issues.Count(i => i.IssueType == RecoveryIssueType.RecallDue),
            RecoveryRate: rate,
            DueTodayValue: patients.Where(p => p.IsDue).Sum(p => p.TotalValue),
            ByIssue: issues.GroupBy(i => i.IssueType)
                .Select(g => new IssueBreakdownDto(g.Key, g.Count(), g.Sum(i => i.Value)))
                .OrderByDescending(x => x.Value).ToList());

        IEnumerable<RecoveryPatientDto> filtered = patients;
        if (query.Priority is not null) filtered = filtered.Where(p => p.Score.Priority == query.Priority);
        if (query.IssueType is not null) filtered = filtered.Where(p => p.Issues.Any(i => i.IssueType == query.IssueType));
        if (query.DueOnly == true) filtered = filtered.Where(p => p.IsDue);
        if (!string.IsNullOrWhiteSpace(query.Stage)) filtered = filtered.Where(p => string.Equals(p.Stage, query.Stage, StringComparison.OrdinalIgnoreCase));

        var list = filtered.Take(Math.Clamp(query.Take ?? 500, 1, 1000)).ToList();
        return new RecoveryDashboardDto(summary, list, await TrendAsync(6, ct), await RecentWinsAsync(ct), now, currency);
    }

    public async Task<RecoveryPatientDto?> GetPatientAsync(Guid patientId, CancellationToken ct)
    {
        var (patients, _) = await BuildPatientsAsync(patientId, ct);
        return patients.FirstOrDefault();
    }

    /// <summary>Core read model: opportunities grouped per patient, joined with follow-up state, scored and ranked.</summary>
    internal async Task<(List<RecoveryPatientDto> Patients, RecoverySettings Settings)> BuildPatientsAsync(Guid? patientId, CancellationToken ct)
    {
        var detection = await detector.DetectAsync(ct, patientId);
        var settings = detection.Settings;
        var now = clock.UtcNow;
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);
        var inactiveRule = detection.Rules.FirstOrDefault(r => r.Trigger == FollowUpTrigger.TreatmentPlanInactive);

        var ids = detection.Opportunities.Select(o => o.PatientId).Distinct().ToList();
        if (ids.Count == 0) return ([], settings);

        var followUps = await db.FollowUps.AsNoTracking().Where(f => ids.Contains(f.PatientId)).ToListAsync(ct);
        var info = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.FullNameAr, p.Phone, p.WhatsAppNumber, p.PreferredLanguage, p.LastVisitAt, p.RegisteredAt })
            .ToDictionaryAsync(p => p.Id, ct);
        var yearAgo = now.AddDays(-365);
        var visitStats = await db.Appointments.AsNoTracking().Where(a => ids.Contains(a.PatientId))
            .GroupBy(a => a.PatientId)
            .Select(g => new
            {
                PatientId = g.Key,
                Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                NoShows = g.Count(a => a.Status == AppointmentStatus.NoShow && a.StartTime >= yearAgo)
            }).ToDictionaryAsync(x => x.PatientId, ct);
        var accepted = (await db.TreatmentPlans.AsNoTracking()
                .Where(t => ids.Contains(t.PatientId) && t.AcceptedAt != null
                            && t.Status != TreatmentPlanStatus.Completed && t.Status != TreatmentPlanStatus.Rejected)
                .Select(t => t.PatientId).Distinct().ToListAsync(ct)).ToHashSet();

        var result = new List<RecoveryPatientDto>();
        foreach (var group in detection.Opportunities.GroupBy(o => o.PatientId))
        {
            if (!info.TryGetValue(group.Key, out var p)) continue;
            var issueDtos = new List<RecoveryIssueDto>();
            DateTime? lastContacted = null;
            var attempts = 0;
            var stageNew = false;
            var dueDates = new List<DateTime>();

            foreach (var o in group)
            {
                var f = MatchFollowUp(followUps, o);
                if (f?.Status == FollowUpStatus.Dismissed) continue;

                var status = f?.Status;
                DateTime due;
                if (f?.LastContactedAt is not null && status is not FollowUpStatus.Pending)
                {
                    var recontact = PlanIssues.Contains(o.IssueType) && inactiveRule is not null
                        ? inactiveRule.DelayDays : settings.RecontactAfterDays;
                    due = f.LastContactedAt.Value.AddDays(recontact);
                    lastContacted = lastContacted is null || f.LastContactedAt > lastContacted ? f.LastContactedAt : lastContacted;
                    attempts = Math.Max(attempts, f.ContactAttempts);
                }
                else
                {
                    due = o.DueAt;
                    stageNew = true;
                }
                dueDates.Add(due);
                issueDtos.Add(new RecoveryIssueDto(o.IssueType, o.Title, o.Detail, o.Value, o.TriggeredAt, due,
                    o.SourceEntityType, o.SourceEntityId, o.TreatmentPlanId, f?.Id, status));
            }
            if (issueDtos.Count == 0) continue;

            var dueAt = dueDates.Min();
            var isDue = dueAt <= now;
            var stage = stageNew && lastContacted is null ? "New"
                : isDue ? "Recontact"
                : "Contacted";

            var lastVisit = p.LastVisitAt;
            var daysSince = lastVisit is null ? (int?)null : (int)(now - lastVisit.Value).TotalDays;
            var stats = visitStats.GetValueOrDefault(p.Id);
            var signals = new PatientSignals(stats?.Completed ?? 0, stats?.NoShows ?? 0,
                daysSince ?? (int)(now - p.RegisteredAt).TotalDays, accepted.Contains(p.Id));

            var total = issueDtos.Sum(i => i.Value);
            var overdueDays = isDue ? (int)(now - dueAt).TotalDays : 0;
            var score = RecoveryScorer.Score(new ScoreInput(total, overdueDays, signals, issueDtos.Select(i => i.IssueType).ToList()), settings, currency);

            var primary = issueDtos.OrderByDescending(i => RecoveryScorer.IssueSeverity[i.IssueType]).ThenByDescending(i => i.Value).First();
            var primaryOpp = group.First(o => o.SourceEntityId == primary.SourceEntityId && o.IssueType == primary.IssueType);

            result.Add(new RecoveryPatientDto(
                p.Id, p.FullName, p.FullNameAr, p.Phone, p.WhatsAppNumber, p.PreferredLanguage, lastVisit, daysSince,
                primary.IssueType, issueDtos.OrderByDescending(i => i.Value).ToList(), total, score, isDue, dueAt, stage,
                lastContacted, attempts, RecommendedAction(primary, primaryOpp, currency, now), TemplateFor(primary.IssueType),
                primaryOpp.DentistId, primaryOpp.TreatmentName, primaryOpp.TreatmentPlanId));
        }

        // Due patients first, then by score, then by value.
        return (result.OrderByDescending(r => r.IsDue).ThenByDescending(r => r.Score.Score).ThenByDescending(r => r.TotalValue).ToList(), settings);
    }

    internal static FollowUp? MatchFollowUp(IEnumerable<FollowUp> followUps, RecoveryOpportunity o) =>
        followUps
            .Where(f => f.IssueType == o.IssueType && f.SourceEntityId == o.SourceEntityId && f.CreatedAt >= o.TriggeredAt.AddMinutes(-1))
            .OrderBy(f => f.Status == FollowUpStatus.Dismissed ? 0 : OpenStatuses.Contains(f.Status) ? 1 : 2)
            .ThenByDescending(f => f.CreatedAt)
            .FirstOrDefault();

    private static string RecommendedAction(RecoveryIssueDto issue, RecoveryOpportunity o, string currency, DateTime now)
    {
        var days = (int)(now - o.TriggeredAt).TotalDays;
        var amount = issue.Value.ToString("#,0.##", CultureInfo.InvariantCulture);
        return issue.IssueType switch
        {
            RecoveryIssueType.TreatmentNotStarted => $"Call to book {o.TreatmentName} — plan waiting {days} days ({amount} {currency})",
            RecoveryIssueType.TreatmentUnfinished => $"Book next visit: {o.TreatmentName} ({amount} {currency} remaining)",
            RecoveryIssueType.MissedAppointment => $"Reschedule missed {o.TreatmentName}",
            RecoveryIssueType.OutstandingBalance => $"Request payment of {amount} {currency}",
            RecoveryIssueType.OverdueFollowUp => "Book clinical review visit",
            RecoveryIssueType.RecallDue => "Offer a check-up & cleaning appointment",
            _ => "Contact patient"
        };
    }

    public static MessageTemplateType TemplateFor(RecoveryIssueType issue) => issue switch
    {
        RecoveryIssueType.MissedAppointment => MessageTemplateType.MissedAppointment,
        RecoveryIssueType.OutstandingBalance => MessageTemplateType.PaymentReminder,
        RecoveryIssueType.RecallDue => MessageTemplateType.Recall,
        RecoveryIssueType.OverdueFollowUp => MessageTemplateType.PostTreatment,
        _ => MessageTemplateType.TreatmentFollowUp
    };

    // ================================================================== actions

    public async Task<ContactPatientResult> ContactAsync(Guid patientId, ContactPatientRequest request, CancellationToken ct)
    {
        var patient = await GetPatientAsync(patientId, ct)
                      ?? throw new BusinessRuleException("This patient has no open recovery items.");

        var followUps = await EnsureFollowUpsAsync(patientId, ct);
        var primary = followUps.FirstOrDefault(f => f.IssueType == patient.PrimaryIssue) ?? followUps.First();
        var primaryIssue = patient.Issues.First(i => i.IssueType == patient.PrimaryIssue);
        var now = clock.UtcNow;

        Guid? messageId = null;
        MessageStatus? status = null;
        string? manualUrl = null, body = null;

        if (request.Channel == MessageChannel.Phone)
        {
            var log = new Message
            {
                PatientId = patientId,
                Channel = MessageChannel.Phone,
                Body = string.IsNullOrWhiteSpace(request.Notes) ? $"Phone call — {primary.Title}" : request.Notes.Trim(),
                ToNumber = patient.Phone,
                Status = MessageStatus.Logged,
                SentAt = now,
                SentById = currentUser.UserId,
                FollowUpId = primary.Id
            };
            db.Messages.Add(log);
            messageId = log.Id;
            status = log.Status;
            body = log.Body;
        }
        else
        {
            var sent = await messaging.SendAsync(new SendMessageRequest(patientId, request.Channel, request.TemplateId,
                request.TemplateId is null && string.IsNullOrWhiteSpace(request.Body) ? request.TemplateType ?? patient.SuggestedTemplate : null,
                request.Body, null, primary.Id,
                patient.PrimaryIssue == RecoveryIssueType.OutstandingBalance ? primaryIssue.Value : null,
                patient.SuggestedTreatment, patient.SuggestedDentistId), ct);
            if (sent.Status == MessageStatus.Failed) throw new BusinessRuleException($"Message failed: {sent.Error}");
            messageId = sent.MessageId;
            status = sent.Status;
            manualUrl = sent.ManualSendUrl;
            body = sent.Body;
        }

        foreach (var f in followUps)
        {
            f.Status = FollowUpStatus.Contacted;
            f.ContactAttempts++;
            f.LastContactedAt = now;
            f.AssignedToUserId ??= currentUser.UserId;
            if (!string.IsNullOrWhiteSpace(request.Notes)) f.Outcome = request.Notes.Trim();
        }
        await db.SaveChangesAsync(ct);
        return new ContactPatientResult(messageId, status, manualUrl, body, followUps.Count);
    }

    public async Task RecordOutcomeAsync(Guid patientId, OutcomeRequest request, CancellationToken ct)
    {
        var followUps = await EnsureFollowUpsAsync(patientId, ct);
        if (request.IssueTypes is { Count: > 0 }) followUps = followUps.Where(f => request.IssueTypes.Contains(f.IssueType)).ToList();
        if (followUps.Count == 0) throw new BusinessRuleException("No open recovery items for this patient.");

        var now = clock.UtcNow;
        foreach (var f in followUps)
        {
            switch (request.Outcome)
            {
                case ContactOutcome.NoResponse:
                    f.Status = FollowUpStatus.NoResponse;
                    f.ContactAttempts++;
                    f.LastContactedAt = now;
                    break;
                case ContactOutcome.CallBackLater:
                    f.Status = FollowUpStatus.Contacted;
                    f.LastContactedAt = now;
                    break;
                case ContactOutcome.Declined:
                case ContactOutcome.AlreadyHandled:
                    f.Status = FollowUpStatus.Dismissed;
                    f.ResolvedAt = now;
                    break;
            }
            f.AssignedToUserId ??= currentUser.UserId;
            f.Outcome = string.IsNullOrWhiteSpace(request.Notes) ? request.Outcome.ToString() : $"{request.Outcome}: {request.Notes.Trim()}";
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Materialises a FollowUp row for every current opportunity of the patient (idempotent) and returns the open ones.
    /// Also used by the background sync.
    /// </summary>
    public async Task<List<FollowUp>> EnsureFollowUpsAsync(Guid patientId, CancellationToken ct)
    {
        var detection = await detector.DetectAsync(ct, patientId);
        var existing = await db.FollowUps.Where(f => f.PatientId == patientId).ToListAsync(ct);
        var open = new List<FollowUp>();
        foreach (var o in detection.Opportunities)
        {
            var f = MatchFollowUp(existing, o);
            if (f?.Status == FollowUpStatus.Dismissed) continue;
            if (f is null)
            {
                f = NewFollowUp(o);
                db.FollowUps.Add(f);
                existing.Add(f);
            }
            else
            {
                f.EstimatedValue = o.Value;
                if (f.Status is FollowUpStatus.Booked or FollowUpStatus.Recovered or FollowUpStatus.Resolved) // booking fell through / still owes
                    f.Status = f.LastContactedAt is null ? FollowUpStatus.Pending : FollowUpStatus.Contacted;
            }
            open.Add(f);
        }
        return open;
    }

    internal FollowUp NewFollowUp(RecoveryOpportunity o) => new()
    {
        PatientId = o.PatientId,
        IssueType = o.IssueType,
        RuleId = o.RuleId,
        SourceEntityType = o.SourceEntityType,
        SourceEntityId = o.SourceEntityId,
        Title = o.Title,
        DueDate = o.DueAt,
        Priority = o.RulePriority,
        EstimatedValue = o.Value,
        Status = FollowUpStatus.Pending,
        CreatedAt = clock.UtcNow
    };

    // ================================================================== analytics helpers

    private sealed record RecoveredRow(DateTime PaidAt, decimal Signed, Guid PatientId);

    private async Task<List<RecoveredRow>> RecoveredBetweenAsync(DateTime from, DateTime to, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .Where(p => p.RecoveredFollowUpId != null && p.PaidAt >= from && p.PaidAt <= to)
            .Select(p => new RecoveredRow(p.PaidAt, p.Type == PaymentType.Refund ? -p.Amount : p.Amount, p.PatientId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RecoveryTrendPointDto>> TrendAsync(int months, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var start = clinicTime.StartOfMonthUtc(today.AddMonths(-(months - 1)));
        var rows = await RecoveredBetweenAsync(start, clock.UtcNow, ct);
        var contacts = await db.Messages.AsNoTracking()
            .Where(m => m.FollowUpId != null && m.CreatedAt >= start)
            .Select(m => new { m.CreatedAt, m.PatientId }).ToListAsync(ct);

        var points = new List<RecoveryTrendPointDto>();
        for (var i = months - 1; i >= 0; i--)
        {
            var monthDate = today.AddMonths(-i);
            var from = clinicTime.StartOfMonthUtc(monthDate);
            var to = clinicTime.StartOfMonthUtc(monthDate.AddMonths(1));
            var inMonth = rows.Where(r => r.PaidAt >= from && r.PaidAt < to).ToList();
            points.Add(new RecoveryTrendPointDto(
                new DateOnly(monthDate.Year, monthDate.Month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                inMonth.Sum(r => r.Signed),
                contacts.Where(c => c.CreatedAt >= from && c.CreatedAt < to).Select(c => c.PatientId).Distinct().Count(),
                inMonth.Where(r => r.Signed > 0).Select(r => r.PatientId).Distinct().Count()));
        }
        return points;
    }

    private async Task<IReadOnlyList<RecoveryWinDto>> RecentWinsAsync(CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        return await db.Payments.AsNoTracking()
            .Where(p => p.RecoveredFollowUpId != null && p.Type == PaymentType.Payment && p.PaidAt >= since)
            .OrderByDescending(p => p.PaidAt).Take(8)
            .Select(p => new RecoveryWinDto(p.PatientId, p.Patient.FullName, p.RecoveredFollowUp!.IssueType, p.Amount, p.PaidAt,
                p.RecoveredFollowUp.AssignedToUser != null ? p.RecoveredFollowUp.AssignedToUser.FullName : null))
            .ToListAsync(ct);
    }
}
