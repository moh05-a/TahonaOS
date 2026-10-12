using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

/// <summary>A configurable automation rule: "when X happens, follow up after N days".</summary>
public class FollowUpRule : TenantEntity, IAuditable
{
    public string Name { get; set; } = default!;
    public FollowUpTrigger Trigger { get; set; }
    public int DelayDays { get; set; }
    /// <summary>For ProcedureCompleted rules: limit to a category (e.g. Endodontic).</summary>
    public ProcedureCategory? ProcedureCategory { get; set; }
    public Guid? MessageTemplateId { get; set; }
    public MessageTemplate? MessageTemplate { get; set; }
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    public bool IsEnabled { get; set; } = true;
    /// <summary>Send the template automatically when a WhatsApp provider is configured.</summary>
    public bool AutoSend { get; set; }
}

/// <summary>A concrete task: contact this patient about this issue.</summary>
public class FollowUp : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public RecoveryIssueType IssueType { get; set; }
    public Guid? RuleId { get; set; }
    public FollowUpRule? Rule { get; set; }
    /// <summary>Entity that triggered this follow-up (Appointment, TreatmentPlan, Invoice, TreatmentRecord, Patient).</summary>
    public string? SourceEntityType { get; set; }
    public Guid? SourceEntityId { get; set; }
    public string Title { get; set; } = default!;
    public DateTime DueDate { get; set; }
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    public decimal EstimatedValue { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }
    public int ContactAttempts { get; set; }
    public DateTime? LastContactedAt { get; set; }
    public string? Outcome { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? BookedAppointmentId { get; set; }
    public decimal RecoveredAmount { get; set; }
}
