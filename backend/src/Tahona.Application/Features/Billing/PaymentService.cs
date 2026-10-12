using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Notifications;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Billing;

public class PaymentService(
    IAppDbContext db,
    ISearchProvider search,
    ISequenceGenerator sequences,
    RecoveryAttributionService attribution,
    NotificationService notifications,
    ICurrentUser currentUser,
    IClinicTime clinicTime,
    IClock clock)
{
    private static readonly InvoiceStatus[] OpenStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid];

    public async Task<PagedResult<PaymentListItemDto>> ListAsync(PaymentQuery query, CancellationToken ct)
    {
        var q = db.Payments.AsNoTracking();
        if (query.PatientId is not null) q = q.Where(p => p.PatientId == query.PatientId);
        if (query.Method is not null) q = q.Where(p => p.Method == query.Method);
        if (query.From is not null) q = q.Where(p => p.PaidAt >= query.From.Value.UtcDateTime);
        if (query.To is not null) q = q.Where(p => p.PaidAt < query.To.Value.UtcDateTime);
        if (query.RecoveredOnly == true) q = q.Where(p => p.RecoveredFollowUpId != null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var patientIds = search.FilterPatients(db.Patients, term).Select(p => p.Id);
            q = q.Where(p => patientIds.Contains(p.PatientId) || p.ReceiptNumber.Contains(term.ToUpper()));
        }

        return await q.OrderByDescending(p => p.PaidAt)
            .Select(p => new PaymentListItemDto(p.Id, p.ReceiptNumber, p.PatientId, p.Patient.FullName, p.InvoiceId,
                p.Invoice != null ? p.Invoice.InvoiceNumber : null, p.Type, p.Amount, p.Method, p.PaidAt, p.Reference,
                p.ReceivedBy != null ? p.ReceivedBy.FullName : null, p.RecoveredFollowUpId != null))
            .ToPagedAsync(query, ct);
    }

    /// <summary>
    /// Records a payment. With an invoice id it is applied to that invoice; without one it is allocated
    /// to the patient's open invoices oldest-first (one receipt line per invoice).
    /// </summary>
    public async Task<RecordPaymentResult> RecordAsync(RecordPaymentRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            throw RequestValidationException.For("patientId", "Patient not found.");

        var now = clock.UtcNow;
        var paidAt = request.PaidAt?.UtcDateTime ?? now;
        if (paidAt > now.AddMinutes(5)) throw RequestValidationException.For("paidAt", "Payment date cannot be in the future.");

        List<Invoice> targets;
        if (request.InvoiceId is not null)
        {
            var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId && i.PatientId == request.PatientId, ct)
                      ?? throw RequestValidationException.For("invoiceId", "Invoice not found for this patient.");
            if (!OpenStatuses.Contains(inv.Status) || inv.Balance <= 0)
                throw new BusinessRuleException($"Invoice {inv.InvoiceNumber} has no outstanding balance.");
            if (request.Amount > inv.Balance)
                throw RequestValidationException.For("amount", $"Amount exceeds the invoice balance ({inv.Balance:0.000}).");
            targets = [inv];
        }
        else
        {
            targets = await db.Invoices.Where(i => i.PatientId == request.PatientId && OpenStatuses.Contains(i.Status) && i.Balance > 0)
                .OrderBy(i => i.DueDate).ThenBy(i => i.IssueDate).ToListAsync(ct);
            if (targets.Count == 0) throw new BusinessRuleException("This patient has no outstanding invoices. Create an invoice first.");
            var totalDue = targets.Sum(i => i.Balance);
            if (request.Amount > totalDue)
                throw RequestValidationException.For("amount", $"Amount exceeds the total outstanding balance ({totalDue:0.000}).");
        }

        var remaining = Money.Round(request.Amount);
        var ids = new List<Guid>();
        var receipts = new List<string>();
        decimal recovered = 0;

        foreach (var inv in targets)
        {
            if (remaining <= 0) break;
            var apply = Math.Min(remaining, inv.Balance);
            var payment = new Payment
            {
                ReceiptNumber = await sequences.NextAsync(SequenceKind.Receipt, ct),
                PatientId = request.PatientId,
                InvoiceId = inv.Id,
                Type = PaymentType.Payment,
                Amount = apply,
                Method = request.Method,
                PaidAt = paidAt,
                Reference = request.Reference?.Trim(),
                Notes = request.Notes?.Trim(),
                ReceivedById = currentUser.UserId
            };

            if (await attribution.AttributePaymentAsync(payment, ct) is not null) recovered += apply;

            db.Payments.Add(payment);
            ApplyToInvoice(inv, apply);
            remaining -= apply;
            ids.Add(payment.Id);
            receipts.Add(payment.ReceiptNumber);
        }

        await db.SaveChangesAsync(ct);
        return new RecordPaymentResult(ids, receipts, Money.Round(request.Amount - remaining), recovered);
    }

    public async Task<Guid> RefundAsync(Guid paymentId, RefundRequest request, CancellationToken ct)
    {
        var original = await db.Payments.Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                       ?? throw new NotFoundException("Payment", paymentId);
        if (original.Type != PaymentType.Payment) throw new BusinessRuleException("Only payments can be refunded.");

        var alreadyRefunded = await db.Payments.Where(p => p.RefundOfPaymentId == paymentId).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var refundable = original.Amount - alreadyRefunded;
        if (request.Amount > refundable)
            throw RequestValidationException.For("amount", $"Maximum refundable amount is {refundable:0.000}.");

        var refund = new Payment
        {
            ReceiptNumber = await sequences.NextAsync(SequenceKind.Receipt, ct),
            PatientId = original.PatientId,
            InvoiceId = original.InvoiceId,
            Type = PaymentType.Refund,
            Amount = Money.Round(request.Amount),
            Method = request.Method ?? original.Method,
            PaidAt = clock.UtcNow,
            Notes = request.Reason.Trim(),
            ReceivedById = currentUser.UserId,
            RefundOfPaymentId = original.Id
        };
        await attribution.ReverseForRefundAsync(original, refund, ct);
        db.Payments.Add(refund);

        if (original.Invoice is not null)
        {
            var inv = original.Invoice;
            inv.AmountPaid = Money.Round(inv.AmountPaid - refund.Amount);
            inv.Balance = Money.Round(inv.Total - inv.AmountPaid);
            inv.Status = inv.AmountPaid <= 0
                ? (alreadyRefunded + refund.Amount >= original.Amount && inv.Total > 0 ? InvoiceStatus.Refunded : InvoiceStatus.Issued)
                : InvoiceStatus.PartiallyPaid;
        }

        await db.SaveChangesAsync(ct);

        var patientName = await db.Patients.Where(p => p.Id == original.PatientId).Select(p => p.FullName).FirstAsync(ct);
        await notifications.NotifyAsync(new NotifyRequest(
            NotificationType.StaffAction, PriorityLevel.Medium,
            $"Refund issued: {refund.Amount:0.###} to {patientName}",
            $"{currentUser.FullName}: {refund.Notes}", $"/patients/{original.PatientId}?tab=payments", nameof(Payment), refund.Id,
            RequiredPermission: Permissions.SettingsManage, ExcludeUserId: currentUser.UserId), ct);
        return refund.Id;
    }

    public async Task<PaymentSummaryDto> SummaryAsync(CancellationToken ct)
    {
        var today = clinicTime.Today;
        var dayStart = clinicTime.StartOfDayUtc(today);
        var monthStart = clinicTime.StartOfMonthUtc(today);

        var month = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= monthStart)
            .Select(p => new { p.Type, p.Amount, p.Method, p.PaidAt, Recovered = p.RecoveredFollowUpId != null })
            .ToListAsync(ct);

        var payments = month.Where(p => p.Type == PaymentType.Payment).ToList();
        var refunds = month.Where(p => p.Type == PaymentType.Refund).ToList();

        return new PaymentSummaryDto(
            payments.Where(p => p.PaidAt >= dayStart).Sum(p => p.Amount) - refunds.Where(p => p.PaidAt >= dayStart).Sum(p => p.Amount),
            payments.Sum(p => p.Amount) - refunds.Sum(p => p.Amount),
            refunds.Sum(p => p.Amount),
            payments.Where(p => p.Recovered).Sum(p => p.Amount) - refunds.Where(p => p.Recovered).Sum(p => p.Amount),
            payments.GroupBy(p => p.Method).Select(g => new MethodTotalDto(g.Key, g.Sum(p => p.Amount), g.Count()))
                .OrderByDescending(m => m.Amount).ToList());
    }

    private static void ApplyToInvoice(Invoice inv, decimal amount)
    {
        inv.AmountPaid = Money.Round(inv.AmountPaid + amount);
        inv.Balance = Money.Round(inv.Total - inv.AmountPaid);
        inv.Status = inv.Balance <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }
}
