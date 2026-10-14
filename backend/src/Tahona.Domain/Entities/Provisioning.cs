using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

/// <summary>
/// An operator's invitation for a clinic to create its account. The link carries a random token; only its hash is
/// stored. Accepting it creates the clinic and its first administrator. Not tenant-owned: no clinic exists yet.
/// </summary>
public class ClinicInvitation : BaseEntity
{
    public string Email { get; set; } = default!;
    public string? ClinicName { get; set; }
    public string? ContactName { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    /// <summary>The clinic created when the invitation was accepted.</summary>
    public Guid? ClinicId { get; set; }
    public string Plan { get; set; } = SubscriptionPlans.Founding;
    public int TrialDays { get; set; } = 30;
    public string? Note { get; set; }
    /// <summary>Who issued it (operator name), for the record.</summary>
    public string CreatedBy { get; set; } = default!;
}

/// <summary>A single-use, expiring token sent to one user (password reset). Stored hashed, like refresh tokens.</summary>
public class UserToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public UserTokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string CreatedBy { get; set; } = default!;
}

public static class SubscriptionPlans
{
    public const string Founding = "Founding";
    public const string Standard = "Standard";
    public static readonly IReadOnlyList<string> All = [Founding, Standard];
}

/// <summary>
/// Where a clinic is in switching to TahonaOS. Steps that can be observed (a patient exists, an invoice exists) are
/// derived from data; the rest are confirmed by a person and recorded here.
/// </summary>
public class OnboardingState
{
    /// <summary>Keys of the steps a staff member confirmed by hand.</summary>
    public List<string> ConfirmedSteps { get; set; } = [];
    /// <summary>Set when the clinic declares it is using TahonaOS for daily work.</summary>
    public DateTime? CompletedAt { get; set; }
}
