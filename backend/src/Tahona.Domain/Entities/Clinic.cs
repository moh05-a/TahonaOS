using Tahona.Domain.Common;
using Tahona.Domain.Enums;

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
    /// <summary>False once the account is archived: nobody can sign in. Suspension is <see cref="SubscriptionStatus"/>.</summary>
    public bool IsActive { get; set; } = true;
    public string SubscriptionPlan { get; set; } = SubscriptionPlans.Founding;
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trial;
    public DateTime? TrialEndsAt { get; set; }
    /// <summary>End of the period the clinic has paid for (confirmed by an operator until a payment provider exists).</summary>
    public DateTime? PaidUntil { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public string? StatusReason { get; set; }
    /// <summary>Sample data for demonstrations; never billed and excluded from business metrics.</summary>
    public bool IsDemo { get; set; }

    public ClinicSettings? Settings { get; set; }
}
