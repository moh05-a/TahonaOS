using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

/// <summary>One row per charted tooth (FDI numbering) per patient. Adult 11–48, primary 51–85.</summary>
public class DentalTooth : TenantEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public int ToothNumber { get; set; }
    public bool IsPrimary { get; set; }
    public string? Notes { get; set; }
    public ICollection<DentalCondition> Conditions { get; set; } = [];
}

public class DentalCondition : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Guid DentalToothId { get; set; }
    public DentalTooth DentalTooth { get; set; } = default!;
    public int ToothNumber { get; set; }
    /// <summary>Surfaces affected, e.g. "MOD" (Mesial, Occlusal, Distal, Buccal, Lingual).</summary>
    public string? Surfaces { get; set; }
    public ToothConditionType Type { get; set; }
    public ConditionStatus Status { get; set; } = ConditionStatus.Diagnosed;
    public string? Notes { get; set; }
    public DateTime RecordedAt { get; set; }
    public Guid? DentistId { get; set; }
    public Dentist? Dentist { get; set; }
    public Guid? TreatmentPlanItemId { get; set; }
}
