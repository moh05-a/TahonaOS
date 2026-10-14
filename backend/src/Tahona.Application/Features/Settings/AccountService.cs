using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Settings;

/// <param name="ReadOnly">The account can be viewed and exported but not changed.</param>
/// <param name="PaymentInstructions">How to pay, as configured for this deployment; null when not set.</param>
public record AccountDto(string ClinicName, string Plan, SubscriptionStatus Status, DateTime? TrialEndsAt, DateTime? PaidUntil, string? StatusReason,
    bool ReadOnly, DateTime CreatedAt, int Patients, int ActiveStaff, int Dentists, string? PaymentInstructions, string? SupportContact);

/// <summary>What a clinic administrator needs to know about their subscription: plan, state, dates, and how to pay.</summary>
public class AccountService(IAppDbContext db, IPlatformInfo platform)
{
    public async Task<AccountDto> GetAsync(CancellationToken ct)
    {
        var c = await db.Clinics.AsNoTracking().FirstAsync(ct);
        return new AccountDto(c.Name, c.SubscriptionPlan, c.SubscriptionStatus, c.TrialEndsAt, c.PaidUntil, c.StatusReason,
            ClinicAccess.IsReadOnly(c.SubscriptionStatus), c.CreatedAt,
            await db.Patients.CountAsync(ct), await db.Users.CountAsync(u => u.IsActive, ct), await db.Dentists.CountAsync(d => d.IsActive, ct),
            platform.PaymentInstructions, platform.SupportContact);
    }
}
