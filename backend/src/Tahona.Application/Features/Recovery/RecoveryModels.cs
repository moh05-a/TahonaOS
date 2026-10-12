using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>A single revenue-leak signal detected for a patient.</summary>
public record RecoveryOpportunity(
    Guid PatientId,
    RecoveryIssueType IssueType,
    string SourceEntityType,
    Guid SourceEntityId,
    string Title,
    string Detail,
    decimal Value,
    /// <summary>When the underlying condition started (plan presented, appointment missed, invoice due...).</summary>
    DateTime TriggeredAt,
    /// <summary>When the follow-up rule says the clinic should act.</summary>
    DateTime DueAt,
    Guid? RuleId,
    PriorityLevel RulePriority,
    Guid? TreatmentPlanId = null,
    string? TreatmentName = null,
    Guid? DentistId = null);

/// <summary>Behavioural signals used to estimate the probability that a patient returns.</summary>
public record PatientSignals(
    int CompletedVisits,
    int NoShows,
    int DaysSinceLastVisit,
    bool HasAcceptedPlan);

public record ScoreInput(
    decimal TotalValue,
    int DaysOverdue,
    PatientSignals Signals,
    IReadOnlyCollection<RecoveryIssueType> Issues);

public record ScoreComponent(string Name, double Raw, double Weight, double Points);

/// <summary>The structured inputs behind the score, so clients can render reasons in any language.</summary>
public record ScoreFacts(decimal TotalValue, int DaysSinceLastVisit, int DaysOverdue, int IssueCount, bool HasAcceptedPlan, int NoShows, int CompletedVisits);

public record RecoveryScore(
    int Score,
    PriorityLevel Priority,
    double ReturnLikelihood,
    string LikelihoodLabel,
    IReadOnlyList<ScoreComponent> Components,
    // English explanation (used by the AI assistant and API consumers); clients localise from Facts.
    IReadOnlyList<string> Reasons,
    ScoreFacts Facts);

// ------------------------------------------------------------------ API DTOs

public record RecoveryIssueDto(
    RecoveryIssueType IssueType, string Title, string Detail, decimal Value, DateTime TriggeredAt, DateTime DueAt,
    string SourceEntityType, Guid SourceEntityId, Guid? TreatmentPlanId, Guid? FollowUpId, FollowUpStatus? FollowUpStatus);

public record RecoveryPatientDto(
    Guid PatientId, string FullName, string? FullNameAr, string Phone, string? WhatsAppNumber, string PreferredLanguage,
    DateTime? LastVisitAt, int? DaysSinceVisit, RecoveryIssueType PrimaryIssue, IReadOnlyList<RecoveryIssueDto> Issues,
    decimal TotalValue, RecoveryScore Score, bool IsDue, DateTime DueAt, string Stage,
    DateTime? LastContactedAt, int ContactAttempts, string RecommendedAction, MessageTemplateType SuggestedTemplate,
    Guid? SuggestedDentistId, string? SuggestedTreatment, Guid? SuggestedPlanId);

public record IssueBreakdownDto(RecoveryIssueType IssueType, int Count, decimal Value);

public record RecoveryTrendPointDto(string Period, decimal Recovered, int PatientsContacted, int PatientsRecovered);

public record RecoveryWinDto(Guid PatientId, string PatientName, RecoveryIssueType IssueType, decimal Amount, DateTime ResolvedAt, string? ContactedBy);

public record RecoverySummaryDto(
    decimal PotentialRevenue,
    decimal RecoveredThisMonth,
    decimal RecoveredLastMonth,
    int PatientsToContact,
    int PatientsInProgress,
    int HighValuePatients,
    int MissedAppointments,
    int UnfinishedTreatments,
    int NotStartedTreatments,
    int OutstandingBalanceCount,
    decimal OutstandingBalanceTotal,
    int OverdueFollowUps,
    int RecallsDue,
    double RecoveryRate,
    decimal DueTodayValue,
    IReadOnlyList<IssueBreakdownDto> ByIssue);

public record RecoveryDashboardDto(
    RecoverySummaryDto Summary,
    IReadOnlyList<RecoveryPatientDto> Patients,
    IReadOnlyList<RecoveryTrendPointDto> Trend,
    IReadOnlyList<RecoveryWinDto> RecentWins,
    DateTime GeneratedAt,
    string Currency);
