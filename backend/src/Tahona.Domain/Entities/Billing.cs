using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class Invoice : TenantEntity, IAuditable
{
    public string InvoiceNumber { get; set; } = default!;
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>The appointment whose fee this invoice charges, when it was made by taking that fee.</summary>
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    public TreatmentPlan? TreatmentPlan { get; set; }
    public Guid? DentistId { get; set; }
    public Dentist? Dentist { get; set; }

    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// True for a balance carried over from the clinic's previous system. It states what the patient owed on the
    /// import date; it is not treatment performed in TahonaOS and is excluded from production figures.
    /// </summary>
    public bool IsOpeningBalance { get; set; }
    public Guid? ImportBatchId { get; set; }

    public ICollection<InvoiceItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public class InvoiceItem : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Guid? ProcedureId { get; set; }
    public Guid? TreatmentPlanItemId { get; set; }
    public int? ToothNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// A payment or refund. An entry made by mistake is cancelled, not erased: the row and its receipt number stay,
/// marked <see cref="IsDeleted"/> with a reason, and stop counting towards any total.
/// </summary>
public class Payment : TenantEntity, IAuditable, ISoftDelete
{
    public string ReceiptNumber { get; set; } = default!;
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public PaymentType Type { get; set; } = PaymentType.Payment;
    /// <summary>Always positive. Refunds are distinguished by <see cref="Type"/>.</summary>
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? ReceivedById { get; set; }
    public User? ReceivedBy { get; set; }
    public Guid? RefundOfPaymentId { get; set; }

    /// <summary>True for an entry cancelled as a mistake. <see cref="DeletedAt"/> is when it was cancelled.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? CancelReason { get; set; }
    public Guid? CancelledById { get; set; }
    public User? CancelledBy { get; set; }

    /// <summary>Set when this payment is attributed to a recovery follow-up (counts as recovered revenue).</summary>
    public Guid? RecoveredFollowUpId { get; set; }
    public FollowUp? RecoveredFollowUp { get; set; }
}

public class Expense : TenantEntity, IAuditable
{
    public ExpenseCategory Category { get; set; }
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string? Vendor { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Reference { get; set; }
    public Guid? RecordedById { get; set; }
}
