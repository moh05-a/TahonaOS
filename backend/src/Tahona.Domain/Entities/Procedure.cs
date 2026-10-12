using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

/// <summary>The clinic price list / service catalogue.</summary>
public class Procedure : TenantEntity, IAuditable
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? NameAr { get; set; }
    public ProcedureCategory Category { get; set; }
    public decimal DefaultPrice { get; set; }
    public int DefaultDurationMinutes { get; set; } = 30;
    /// <summary>Tooth condition recorded on the chart when this procedure is completed.</summary>
    public ToothConditionType? ResultingCondition { get; set; }
    public bool IsActive { get; set; } = true;
}
