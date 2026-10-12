using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>
/// Decides whether revenue counts as "recovered". Rule: a payment is recovered revenue when the clinic
/// contacted the patient about a recovery issue within <see cref="RecoverySettings.AttributionWindowDays"/>
/// before the payment. Patients who return without any outreach are not credited to the engine.
/// </summary>
public class RecoveryAttributionService(IAppDbContext db, IClock clock)
{
    private static readonly FollowUpStatus[] AttributableStatuses =
        [FollowUpStatus.Contacted, FollowUpStatus.Booked, FollowUpStatus.NoResponse, FollowUpStatus.Recovered];

    /// <summary>Attributes a (not yet saved) payment. Returns the follow-up it was attributed to, if any.</summary>
    public async Task<FollowUp?> AttributePaymentAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Type != PaymentType.Payment) return null;

        var settings = await db.ClinicSettings.AsNoTracking().Select(s => s.Recovery).FirstAsync(ct);
        var windowStart = payment.PaidAt.AddDays(-settings.AttributionWindowDays);

        var followUp = await db.FollowUps
            .Where(f => f.PatientId == payment.PatientId && AttributableStatuses.Contains(f.Status)
                        && f.LastContactedAt != null && f.LastContactedAt >= windowStart && f.LastContactedAt <= payment.PaidAt)
            .OrderByDescending(f => f.LastContactedAt)
            .FirstOrDefaultAsync(ct);
        if (followUp is null) return null;

        payment.RecoveredFollowUpId = followUp.Id;
        followUp.RecoveredAmount += payment.Amount;
        followUp.Status = FollowUpStatus.Recovered;
        followUp.ResolvedAt ??= clock.UtcNow;
        followUp.Outcome = "Revenue recovered";
        return followUp;
    }

    /// <summary>Reverses attribution for a refund of a recovered payment.</summary>
    public async Task ReverseForRefundAsync(Payment original, Payment refund, CancellationToken ct)
    {
        if (original.RecoveredFollowUpId is null) return;
        refund.RecoveredFollowUpId = original.RecoveredFollowUpId;
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == original.RecoveredFollowUpId, ct);
        if (followUp is not null) followUp.RecoveredAmount = Math.Max(0, followUp.RecoveredAmount - refund.Amount);
    }
}
