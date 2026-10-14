using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>
/// Decides whether a payment is counted as collected after follow-up. Three conditions must all hold:
///  1. the clinic contacted the patient about a recovery issue within <see cref="RecoverySettings.AttributionWindowDays"/>
///     before the payment;
///  2. the payment settles an invoice that belongs to that same issue (see <see cref="Relates"/>);
///  3. the follow-up has not been dismissed.
/// This records a sequence (contact, then payment for the same matter). It does not prove the contact caused the payment,
/// and patients who return without any outreach are never credited to the engine.
/// </summary>
public class RecoveryAttributionService(IAppDbContext db, IClock clock)
{
    private static readonly FollowUpStatus[] AttributableStatuses =
        [FollowUpStatus.Contacted, FollowUpStatus.Booked, FollowUpStatus.NoResponse, FollowUpStatus.Recovered];

    /// <summary>Attributes a (not yet saved) payment on <paramref name="invoice"/>. Returns the follow-up it was attributed to, if any.</summary>
    public async Task<FollowUp?> AttributePaymentAsync(Payment payment, Invoice invoice, CancellationToken ct)
    {
        if (payment.Type != PaymentType.Payment) return null;

        var settings = await db.ClinicSettings.AsNoTracking().Select(s => s.Recovery).FirstAsync(ct);
        var windowStart = payment.PaidAt.AddDays(-settings.AttributionWindowDays);

        var candidates = await db.FollowUps
            .Where(f => f.PatientId == payment.PatientId && AttributableStatuses.Contains(f.Status)
                        && f.LastContactedAt != null && f.LastContactedAt >= windowStart && f.LastContactedAt <= payment.PaidAt)
            .OrderByDescending(f => f.LastContactedAt)
            .ToListAsync(ct);
        var followUp = candidates.FirstOrDefault(f => Relates(f, invoice));
        if (followUp is null) return null;

        payment.RecoveredFollowUpId = followUp.Id;
        followUp.RecoveredAmount += payment.Amount;
        followUp.Status = FollowUpStatus.Recovered;
        followUp.ResolvedAt ??= clock.UtcNow;
        followUp.Outcome = "Paid after follow-up";
        return followUp;
    }

    /// <summary>
    /// Whether an invoice belongs to the matter a follow-up was about:
    ///  - outstanding balance: an invoice that already existed when the clinic made contact;
    ///  - treatment not started / unfinished: an invoice for that treatment plan, issued after the contact;
    ///  - missed appointment, recall, clinical review: an invoice issued after the contact, once a visit was booked.
    /// </summary>
    public static bool Relates(FollowUp followUp, Invoice invoice)
    {
        if (followUp.LastContactedAt is not { } contactedAt) return false;
        return followUp.IssueType switch
        {
            RecoveryIssueType.OutstandingBalance => invoice.IssueDate <= contactedAt,
            RecoveryIssueType.TreatmentNotStarted or RecoveryIssueType.TreatmentUnfinished =>
                invoice.TreatmentPlanId is not null && invoice.TreatmentPlanId == followUp.SourceEntityId && invoice.IssueDate >= contactedAt,
            _ => followUp.BookedAppointmentId is not null && invoice.IssueDate >= contactedAt
        };
    }

    /// <summary>Reverses attribution for a refund of a recovered payment.</summary>
    public async Task ReverseForRefundAsync(Payment original, Payment refund, CancellationToken ct)
    {
        if (original.RecoveredFollowUpId is null) return;
        refund.RecoveredFollowUpId = original.RecoveredFollowUpId;
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == original.RecoveredFollowUpId, ct);
        if (followUp is not null) followUp.RecoveredAmount = Math.Max(0, followUp.RecoveredAmount - refund.Amount);
    }

    /// <summary>
    /// Takes back the credit for a payment that was cancelled as a mistake. Unlike a refund, the money was never
    /// received, so a follow-up credited with nothing else goes back to where it stood before.
    /// </summary>
    public async Task ReverseForCancellationAsync(Payment payment, CancellationToken ct)
    {
        if (payment.RecoveredFollowUpId is null) return;
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == payment.RecoveredFollowUpId, ct);
        if (followUp is null) return;

        followUp.RecoveredAmount = Math.Max(0, followUp.RecoveredAmount - payment.Amount);
        if (followUp.RecoveredAmount > 0 || followUp.Status != FollowUpStatus.Recovered) return;
        followUp.Status = followUp.BookedAppointmentId is null ? FollowUpStatus.Contacted : FollowUpStatus.Booked;
        followUp.ResolvedAt = null;
        followUp.Outcome = null;
    }
}
