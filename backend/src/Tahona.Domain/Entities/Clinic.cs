using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

/// <summary>The tenant root. Every clinic-owned entity references a clinic.</summary>
public class Clinic : BaseEntity
{
    public string Name { get; set; } = default!;
    public string? NameAr { get; set; }
    public string Slug { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "JO";
    public string TimeZone { get; set; } = "Asia/Amman";
    public string Currency { get; set; } = "JOD";
    public string DefaultLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public string SubscriptionPlan { get; set; } = "Professional";

    public ClinicSettings? Settings { get; set; }
}
