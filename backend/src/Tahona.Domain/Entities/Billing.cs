using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class Invoice : TenantEntity, IAuditable
{
    public string InvoiceNumber { get; set; } = default!;
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid? TreatmentPlanId { get; set; }
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

public class Payment : TenantEntity, IAuditable
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
