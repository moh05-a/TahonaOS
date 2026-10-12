using System.Globalization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>
/// Revenue Recovery priority score (0–100). Pure and deterministic so it can be unit tested and explained.
///
/// score = 100 × Σ(weightᵢ × factorᵢ) / Σ weightᵢ, with four factors in [0, 1]:
///   Value       – min(1, value / HighValueThreshold)^0.6           (bigger opportunities first, with diminishing returns)
///   Urgency     – min(1, daysOverdue / 21)                          (how long past the follow-up due date)
///   Likelihood  – 0.5·recency + 0.3·loyalty + 0.2·reliability (+0.1 if a plan was accepted)
///                 recency = e^(−daysSinceLastVisit / 120), loyalty = min(1, completedVisits / 5),
///                 reliability = 1 − min(1, noShows × 0.25)
///   Severity    – max issue severity + 0.1 per additional issue type (capped at 1)
///
/// Weights and the HIGH / MEDIUM thresholds come from <see cref="RecoverySettings"/>. See docs/RECOVERY_ALGORITHM.md.
/// </summary>
public static class RecoveryScorer
{
    public static readonly IReadOnlyDictionary<RecoveryIssueType, double> IssueSeverity = new Dictionary<RecoveryIssueType, double>
    {
        [RecoveryIssueType.TreatmentUnfinished] = 1.0,
        [RecoveryIssueType.TreatmentNotStarted] = 0.8,
        [RecoveryIssueType.MissedAppointment] = 0.75,
        [RecoveryIssueType.OutstandingBalance] = 0.7,
        [RecoveryIssueType.OverdueFollowUp] = 0.65,
        [RecoveryIssueType.RecallDue] = 0.4
    };

    public const double RecencyDecayDays = 120;
    public const double UrgencyFullDays = 21;
    public const int LoyaltyFullVisits = 5;

    public static RecoveryScore Score(ScoreInput input, RecoverySettings settings, string currency = "JOD")
    {
        var threshold = (double)Math.Max(1m, settings.HighValueThreshold);
        var value = Math.Pow(Math.Min(1, (double)Math.Max(0, input.TotalValue) / threshold), 0.6);
        var urgency = Math.Clamp(input.DaysOverdue / UrgencyFullDays, 0, 1);
        var likelihood = ReturnLikelihood(input.Signals);
        var severity = Severity(input.Issues);

        var components = new List<(string Name, double Raw, double Weight)>
        {
            ("Value", value, settings.WeightValue),
            ("Urgency", urgency, settings.WeightUrgency),
            ("Likelihood", likelihood, settings.WeightLikelihood),
            ("Severity", severity, settings.WeightSeverity)
        };

        var totalWeight = components.Sum(c => Math.Max(0, c.Weight));
        if (totalWeight <= 0) totalWeight = 1;
        var scored = components
            .Select(c => new ScoreComponent(c.Name, Math.Round(c.Raw, 3), c.Weight, Math.Round(100 * Math.Max(0, c.Weight) * c.Raw / totalWeight, 1)))
            .ToList();
        var score = (int)Math.Round(Math.Clamp(scored.Sum(c => c.Points), 0, 100));

        var priority = score >= settings.HighPriorityThreshold ? PriorityLevel.High
            : score >= settings.MediumPriorityThreshold ? PriorityLevel.Medium
            : PriorityLevel.Low;

        var facts = new ScoreFacts(input.TotalValue, input.Signals.DaysSinceLastVisit, input.DaysOverdue, input.Issues.Distinct().Count(),
            input.Signals.HasAcceptedPlan, input.Signals.NoShows, input.Signals.CompletedVisits);
        return new RecoveryScore(score, priority, Math.Round(likelihood, 3), LikelihoodLabel(likelihood), scored,
            BuildReasons(input, likelihood, currency), facts);
    }

    public static double ReturnLikelihood(PatientSignals s)
    {
        var recency = Math.Exp(-Math.Max(0, s.DaysSinceLastVisit) / RecencyDecayDays);
        var loyalty = Math.Min(1, s.CompletedVisits / (double)LoyaltyFullVisits);
        var reliability = 1 - Math.Min(1, s.NoShows * 0.25);
        var likelihood = 0.5 * recency + 0.3 * loyalty + 0.2 * reliability + (s.HasAcceptedPlan ? 0.1 : 0);
        return Math.Clamp(likelihood, 0, 1);
    }

    public static string LikelihoodLabel(double likelihood) => likelihood switch
    {
        >= 0.65 => "High",
        >= 0.4 => "Medium",
        _ => "Low"
    };

    public static double Severity(IReadOnlyCollection<RecoveryIssueType> issues)
    {
        if (issues.Count == 0) return 0;
        var distinct = issues.Distinct().ToList();
        return Math.Min(1, distinct.Max(i => IssueSeverity[i]) + 0.1 * (distinct.Count - 1));
    }

    private static List<string> BuildReasons(ScoreInput input, double likelihood, string currency)
    {
        var reasons = new List<string>();
        if (input.TotalValue > 0)
            reasons.Add($"{input.TotalValue.ToString("#,0.##", CultureInfo.InvariantCulture)} {currency} at stake");
        if (input.Signals.DaysSinceLastVisit < int.MaxValue / 2)
            reasons.Add($"{input.Signals.DaysSinceLastVisit} days since last visit");
        if (input.DaysOverdue > 0) reasons.Add($"Follow-up overdue by {input.DaysOverdue} days");
        if (input.Issues.Distinct().Count() > 1) reasons.Add($"{input.Issues.Distinct().Count()} open issues");
        if (input.Signals.HasAcceptedPlan) reasons.Add("Patient accepted the treatment plan");
        if (input.Signals.NoShows > 0) reasons.Add($"{input.Signals.NoShows} past no-show{(input.Signals.NoShows > 1 ? "s" : "")}");
        reasons.Add($"{LikelihoodLabel(likelihood)} probability of returning ({input.Signals.CompletedVisits} completed visits)");
        return reasons;
    }
}
