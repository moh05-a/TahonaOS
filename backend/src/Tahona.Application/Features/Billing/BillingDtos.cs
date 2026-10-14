using FluentValidation;
using Tahona.Application.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Billing;

public record InvoiceQuery : PageQuery
{
    public string? Search { get; init; }
    public InvoiceStatus? Status { get; init; }
    public Guid? PatientId { get; init; }
    public bool? OverdueOnly { get; init; }
    public bool? OpenOnly { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public record InvoiceListItemDto(
    Guid Id, string InvoiceNumber, Guid PatientId, string PatientName, string? PatientNameAr, string PatientPhone,
    DateTime IssueDate, DateTime DueDate, decimal Total, decimal AmountPaid, decimal Balance, InvoiceStatus Status,
    bool IsOverdue, int DaysOverdue, string? PlanNumber, Guid? TreatmentPlanId);

public record InvoiceItemDto(Guid Id, string Description, Guid? ProcedureId, Guid? TreatmentPlanItemId, int? ToothNumber,
    int Quantity, decimal UnitPrice, decimal Discount, decimal Total);

public record InvoicePaymentDto(Guid Id, string ReceiptNumber, PaymentType Type, decimal Amount, PaymentMethod Method,
    DateTime PaidAt, string? Reference, string? ReceivedBy, bool IsRecovered);

public record InvoiceDetailDto(
    InvoiceListItemDto Summary, string? DentistName, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount,
    string? Notes, IReadOnlyList<InvoiceItemDto> Items, IReadOnlyList<InvoicePaymentDto> Payments);

public record InvoiceItemRequest(
    string Description, Guid? ProcedureId, Guid? TreatmentPlanItemId, int? ToothNumber, int Quantity, decimal UnitPrice, decimal Discount);

public record CreateInvoiceRequest(
    Guid PatientId, Guid? DentistId, Guid? TreatmentPlanId, IReadOnlyList<InvoiceItemRequest> Items,
    decimal DiscountAmount, DateTimeOffset? DueDate, string? Notes, bool SaveAsDraft = false);

public record InvoiceFromPlanRequest(IReadOnlyList<Guid>? ItemIds, bool IncludePlannedItems = false, DateTimeOffset? DueDate = null);

public record VoidInvoiceRequest(string Reason);

public record PatientBalanceDto(
    Guid PatientId, string PatientName, string? PatientNameAr, string Phone, decimal Balance, int OpenInvoices,
    DateTime OldestDueDate, int DaysOverdue, DateTime? LastPaymentAt);

public record PaymentQuery : PageQuery
{
    public Guid? PatientId { get; init; }
    public PaymentMethod? Method { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public bool? RecoveredOnly { get; init; }
    public string? Search { get; init; }
    /// <summary>True lists only entries that were cancelled as mistakes. They never appear otherwise.</summary>
    public bool? Cancelled { get; init; }
}

public record PaymentListItemDto(
    Guid Id, string ReceiptNumber, Guid PatientId, string PatientName, Guid? InvoiceId, string? InvoiceNumber,
    PaymentType Type, decimal Amount, PaymentMethod Method, DateTime PaidAt, string? Reference, string? ReceivedBy, bool IsRecovered,
    string? Notes, decimal RefundedAmount, bool IsCancelled, DateTime? CancelledAt, string? CancelledBy, string? CancelReason);

/// <summary>
/// A charge that has no invoice yet, taken together with its payment: a walk-in visit, or treatment nobody invoiced.
/// It becomes an issued invoice with one line.
/// </summary>
public record NewChargeRequest(string Description, Guid? ProcedureId, decimal Total, Guid? DentistId);

public record RecordPaymentRequest(
    Guid PatientId, Guid? InvoiceId, decimal Amount, PaymentMethod Method, DateTimeOffset? PaidAt, string? Reference, string? Notes,
    NewChargeRequest? NewCharge = null);

/// <summary>Corrects how a payment was recorded. The amount, patient and invoice cannot change: cancel the entry and record it again.</summary>
public record UpdatePaymentRequest(PaymentMethod Method, DateTimeOffset PaidAt, string? Reference, string? Notes);

public record CancelPaymentRequest(string Reason);

public record RecordPaymentResult(IReadOnlyList<Guid> PaymentIds, IReadOnlyList<string> ReceiptNumbers, decimal Applied, decimal RecoveredAmount);

public record RefundRequest(decimal Amount, string Reason, PaymentMethod? Method);

public record PaymentSummaryDto(decimal Today, decimal ThisMonth, decimal RefundsThisMonth, decimal RecoveredThisMonth,
    IReadOnlyList<MethodTotalDto> ByMethod);

public record MethodTotalDto(PaymentMethod Method, decimal Amount, int Count);

public record ExpenseQuery : PageQuery
{
    public ExpenseCategory? Category { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public record ExpenseDto(Guid Id, ExpenseCategory Category, string Description, decimal Amount, DateTime ExpenseDate,
    string? Vendor, PaymentMethod PaymentMethod, string? Reference);

public record ExpenseRequest(ExpenseCategory Category, string Description, decimal Amount, DateTimeOffset ExpenseDate,
    string? Vendor, PaymentMethod PaymentMethod, string? Reference);

public class InvoiceItemRequestValidator : AbstractValidator<InvoiceItemRequest>
{
    public InvoiceItemRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 100);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.Discount <= x.UnitPrice * x.Quantity).WithMessage("Discount cannot exceed the line amount.");
    }
}

public class CreateInvoiceValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one line item.");
        RuleForEach(x => x.Items).SetValidator(new InvoiceItemRequestValidator());
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class VoidInvoiceValidator : AbstractValidator<VoidInvoiceRequest>
{
    public VoidInvoiceValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public class RecordPaymentValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).LessThan(1_000_000);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        When(x => x.NewCharge is not null, () =>
        {
            RuleFor(x => x.NewCharge!.Description).NotEmpty().MaximumLength(200);
            RuleFor(x => x.NewCharge!.Total).GreaterThan(0).LessThan(1_000_000);
        });
    }
}

public class UpdatePaymentValidator : AbstractValidator<UpdatePaymentRequest>
{
    public UpdatePaymentValidator()
    {
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class CancelPaymentValidator : AbstractValidator<CancelPaymentRequest>
{
    public CancelPaymentValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public class RefundValidator : AbstractValidator<RefundRequest>
{
    public RefundValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class ExpenseRequestValidator : AbstractValidator<ExpenseRequest>
{
    public ExpenseRequestValidator()
    {
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Amount).GreaterThan(0).LessThan(10_000_000);
        RuleFor(x => x.Vendor).MaximumLength(150);
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.PaymentMethod).IsInEnum();
    }
}
