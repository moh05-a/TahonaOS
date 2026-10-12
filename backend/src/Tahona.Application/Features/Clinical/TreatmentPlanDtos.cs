using FluentValidation;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Clinical;

public record TreatmentPlanQuery : PageQuery
{
    public string? Search { get; init; }
    public TreatmentPlanStatus? Status { get; init; }
    public Guid? PatientId { get; init; }
    public Guid? DentistId { get; init; }
    /// <summary>Only plans with no upcoming appointment and remaining value (recovery candidates).</summary>
    public bool? Stalled { get; init; }
}

public record TreatmentPlanListItemDto(
    Guid Id, string PlanNumber, Guid PatientId, string PatientName, string? PatientNameAr, string PatientPhone,
    Guid DentistId, string DentistName, string Title, TreatmentPlanStatus Status,
    decimal TotalAmount, decimal PaidAmount, decimal RemainingAmount, decimal CompletedValue, int CompletionPercent,
    int ItemCount, int CompletedItems, DateTime CreatedAt, DateTime? PresentedAt, DateTime? LastActivityAt,
    DateTime? NextAppointmentAt, int DaysSinceActivity);

public record TreatmentPlanItemDto(
    Guid Id, Guid? ProcedureId, string Name, int? ToothNumber, string? Surfaces, int Quantity, decimal UnitPrice,
    decimal Discount, decimal Total, TreatmentItemStatus Status, int Sequence, DateTime? CompletedAt, bool IsInvoiced);

public record LinkedInvoiceDto(Guid Id, string InvoiceNumber, decimal Total, decimal AmountPaid, decimal Balance, InvoiceStatus Status, DateTime IssueDate);

public record LinkedAppointmentDto(Guid Id, DateTime StartTime, string Treatment, AppointmentStatus Status, string DentistName);

public record TreatmentPlanDetailDto(
    TreatmentPlanListItemDto Summary, string? Notes, decimal DiscountAmount, decimal Subtotal,
    DateTime? AcceptedAt, DateTime? StartedAt, DateTime? CompletedAt, DateTime? RejectedAt, string? RejectionReason,
    IReadOnlyList<TreatmentPlanItemDto> Items, IReadOnlyList<LinkedInvoiceDto> Invoices,
    IReadOnlyList<LinkedAppointmentDto> Appointments, decimal UninvoicedCompletedValue);

public record TreatmentPlanItemRequest(
    Guid? Id, Guid? ProcedureId, string? Name, int? ToothNumber, string? Surfaces, int Quantity, decimal? UnitPrice, decimal Discount);

public record TreatmentPlanUpsertRequest(
    Guid PatientId, Guid DentistId, string Title, string? Notes, decimal DiscountAmount,
    IReadOnlyList<TreatmentPlanItemRequest> Items, bool Present = false);

public record PlanStatusRequest(TreatmentPlanStatus Status, string? Reason);

public record CompleteItemRequest(DateTimeOffset? PerformedAt, Guid? AppointmentId, Guid? DentistId, string? Notes);

public class TreatmentPlanItemRequestValidator : AbstractValidator<TreatmentPlanItemRequest>
{
    public TreatmentPlanItemRequestValidator()
    {
        RuleFor(x => x).Must(x => x.ProcedureId is not null || !string.IsNullOrWhiteSpace(x.Name))
            .WithMessage("Each item needs a procedure or a name.");
        RuleFor(x => x.Name).MaximumLength(150);
        RuleFor(x => x.ToothNumber).Must(t => t is null || Teeth.IsValid(t.Value)).WithMessage("Invalid FDI tooth number.");
        RuleFor(x => x.Surfaces).MaximumLength(10).Must(Teeth.IsValidSurfaces).WithMessage("Surfaces must use M, O, D, B, L, I, F.");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 50);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000).When(x => x.UnitPrice.HasValue);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0);
    }
}

public class TreatmentPlanUpsertValidator : AbstractValidator<TreatmentPlanUpsertRequest>
{
    public TreatmentPlanUpsertValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DentistId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one treatment item.");
        RuleForEach(x => x.Items).SetValidator(new TreatmentPlanItemRequestValidator());
    }
}
