using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

public class ClinicSettings : TenantEntity, IAuditable
{
    public TimeOnly WorkingHoursStart { get; set; } = new(9, 0);
    public TimeOnly WorkingHoursEnd { get; set; } = new(20, 0);
    /// <summary>Comma separated DayOfWeek values the clinic is open (Jordan default: Sat–Thu).</summary>
    public string WorkingDays { get; set; } = "6,0,1,2,3,4";
    /// <summary>Dates the clinic is closed (public holidays, leave). Booking on one asks for confirmation.</summary>
    public List<DateOnly> Holidays { get; set; } = [];
    public int SlotMinutes { get; set; } = 30;
    public int AppointmentReminderHours { get; set; } = 24;

    /// <summary>Automated messages are never sent between these clinic-local times (the window may cross midnight).</summary>
    public TimeOnly QuietHoursStart { get; set; } = new(21, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(9, 0);
    /// <summary>When true, automated messages only go to patients whose consent is recorded as Granted.</summary>
    public bool RequireConsentForAutomation { get; set; } = true;

    public string InvoicePrefix { get; set; } = "INV";
    public int NextInvoiceNumber { get; set; } = 1001;
    public int NextReceiptNumber { get; set; } = 5001;
    public int InvoiceDueDays { get; set; } = 14;
    public decimal TaxRatePercent { get; set; }
    public int NextPatientNumber { get; set; } = 1;
    public int NextPlanNumber { get; set; } = 1;

    public bool WhatsAppEnabled { get; set; } = true;
    public bool AutoSendFollowUps { get; set; }
    public bool AiAssistantEnabled { get; set; } = true;

    public RecoverySettings Recovery { get; set; } = new();
    public OnboardingState Onboarding { get; set; } = new();
}

/// <summary>
/// Tunables for the Revenue Recovery Engine and its priority score.
/// Detection timing (e.g. "plan not started after 3 days") lives in <see cref="FollowUpRule"/>s;
/// these settings control valuation, look-back, attribution and scoring.
/// See docs/RECOVERY_ALGORITHM.md for the meaning of each value.
/// </summary>
public class RecoverySettings
{
    // Look-back windows (days): older issues are considered lost and are not surfaced.
    public int MissedAppointmentLookbackDays { get; set; } = 90;
    public int MaxLookbackDays { get; set; } = 540;
    /// <summary>After a contact with no outcome, the patient re-enters the action list after this many days.</summary>
    public int RecontactAfterDays { get; set; } = 5;

    // Valuation (clinic currency)
    public decimal RecallEstimatedValue { get; set; } = 35m;
    public decimal MissedAppointmentDefaultValue { get; set; } = 40m;
    public decimal FollowUpVisitValue { get; set; } = 25m;
    public decimal HighValueThreshold { get; set; } = 400m;

    // Attribution: a payment within this window after a recovery contact counts as recovered revenue.
    public int AttributionWindowDays { get; set; } = 45;

    // Score weights (relative, normalised at runtime)
    public double WeightValue { get; set; } = 35;
    public double WeightUrgency { get; set; } = 20;
    public double WeightLikelihood { get; set; } = 25;
    public double WeightSeverity { get; set; } = 20;

    // Priority thresholds on the 0–100 score
    public int HighPriorityThreshold { get; set; } = 70;
    public int MediumPriorityThreshold { get; set; } = 45;
}
