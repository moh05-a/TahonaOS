using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class Appointment : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid DentistId { get; set; }
    public Dentist Dentist { get; set; } = default!;

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Treatment { get; set; } = default!;
    public Guid? TreatmentPlanId { get; set; }
    public TreatmentPlan? TreatmentPlan { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public string? Notes { get; set; }
    public string? Chair { get; set; }

    /// <summary>
    /// What the patient is expected to pay for this visit, agreed when booking. It is not a debt: nothing is
    /// invoiced until payment is taken, so a cancelled or missed visit owes nothing.
    /// </summary>
    public decimal? Fee { get; set; }
    /// <summary>The invoice made when the fee was taken, if any (normally one; voided ones are ignored).</summary>
    public List<Invoice> Invoices { get; set; } = [];

    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    /// <summary>Automatic reminder attempts so far; automation stops after a small fixed number.</summary>
    public int ReminderAttempts { get; set; }

    /// <summary>The appointment's identifier in the system it was imported from.</summary>
    public string? ExternalId { get; set; }
    public Guid? ImportBatchId { get; set; }

    /// <summary>Set when the booking came from a recovery follow-up, for attribution.</summary>
    public Guid? FollowUpId { get; set; }
}
