using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class TreatmentPlan : TenantEntity, IAuditable
{
    public string PlanNumber { get; set; } = default!;
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid DentistId { get; set; }
    public Dentist Dentist { get; set; } = default!;
    public string Title { get; set; } = default!;
    public TreatmentPlanStatus Status { get; set; } = TreatmentPlanStatus.Draft;
    public string? Notes { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>Denormalised sum of non-cancelled item totals minus discount.</summary>
    public decimal TotalAmount { get; set; }

    public DateTime? PresentedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>Last clinical activity on the plan (item completed / appointment attended).</summary>
    public DateTime? LastActivityAt { get; set; }

    public ICollection<TreatmentPlanItem> Items { get; set; } = [];
}

public class TreatmentPlanItem : TenantEntity, IAuditable
{
    public Guid TreatmentPlanId { get; set; }
    public TreatmentPlan TreatmentPlan { get; set; } = default!;
    public Guid? ProcedureId { get; set; }
    public Procedure? Procedure { get; set; }
    public string Name { get; set; } = default!;
    public int? ToothNumber { get; set; }
    public string? Surfaces { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public TreatmentItemStatus Status { get; set; } = TreatmentItemStatus.Planned;
    public int Sequence { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? AppointmentId { get; set; }
    /// <summary>True once the item has been added to an invoice.</summary>
    public bool IsInvoiced { get; set; }
}

/// <summary>Record of a procedure performed on a patient (treatment history).</summary>
public class TreatmentRecord : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid DentistId { get; set; }
    public Dentist Dentist { get; set; } = default!;
    public Guid? AppointmentId { get; set; }
    public Guid? TreatmentPlanItemId { get; set; }
    public Guid? ProcedureId { get; set; }
    public Procedure? Procedure { get; set; }
    public string ProcedureName { get; set; } = default!;
    public ProcedureCategory? Category { get; set; }
    public int? ToothNumber { get; set; }
    public string? Surfaces { get; set; }
    public DateTime PerformedAt { get; set; }
    public decimal Price { get; set; }
    public string? Notes { get; set; }
}
