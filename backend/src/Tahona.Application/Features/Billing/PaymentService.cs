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
    InvoiceService invoices,
    RecoveryAttributionService attribution,
    NotificationService notifications,
    ICurrentUser currentUser,
    IClinicTime clinicTime,
    IClock clock)
{
    private static readonly InvoiceStatus[] OpenStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid];

    public async Task<PagedResult<PaymentListItemDto>> ListAsync(PaymentQuery query, CancellationToken ct)
    {
        // Cancelled entries sit behind the soft-delete filter. Listing them means lifting every filter,
        // so the clinic is restricted by hand here.
        var q = query.Cancelled == true
            ? db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.ClinicId == currentUser.ClinicId && p.IsDeleted)
            : db.Payments.AsNoTracking();
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
                p.ReceivedBy != null ? p.ReceivedBy.FullName : null, p.RecoveredFollowUpId != null && !p.IsDeleted,
                p.Notes, db.Payments.Where(r => r.RefundOfPaymentId == p.Id).Sum(r => (decimal?)r.Amount) ?? 0,
                p.IsDeleted, p.DeletedAt, p.CancelledBy != null ? p.CancelledBy.FullName : null, p.CancelReason))
            .ToPagedAsync(query, ct);
    }

    /// <summary>
    /// Records a payment. With an invoice id it is applied to that invoice; with a new charge an invoice is
    /// created for it in the same step; with neither it is allocated to the patient's open invoices
    /// oldest-first (one receipt line per invoice).
    /// </summary>
    public async Task<RecordPaymentResult> RecordAsync(RecordPaymentRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            throw RequestValidationException.For("patientId", "Patient not found.");

        var now = clock.UtcNow;
        var paidAt = request.PaidAt?.UtcDateTime ?? now;
        if (paidAt > now.AddMinutes(5)) throw RequestValidationException.For("paidAt", "Payment date cannot be in the future.");

        List<Invoice> targets;
        if (request.NewCharge is { } charge)
        {
            if (request.InvoiceId is not null)
                throw RequestValidationException.For("invoiceId", "Choose an existing invoice or a new charge, not both.");
            if (charge.DentistId is not null && !await db.Dentists.AnyAsync(d => d.Id == charge.DentistId, ct))
                throw RequestValidationException.For("newCharge.dentistId", "Dentist not found.");
            if (charge.ProcedureId is not null && !await db.Procedures.AnyAsync(p => p.Id == charge.ProcedureId, ct))
                throw RequestValidationException.For("newCharge.procedureId", "Procedure not found.");

            var dentistId = charge.DentistId;
            if (charge.AppointmentId is not null)
            {
                var appointment = await db.Appointments.Where(a => a.Id == charge.AppointmentId && a.PatientId == request.PatientId)
                                      .Select(a => new { a.DentistId }).FirstOrDefaultAsync(ct)
                                  ?? throw RequestValidationException.For("newCharge.appointmentId", "Appointment not found for this patient.");
                var existing = await db.Invoices.Where(i => i.AppointmentId == charge.AppointmentId && i.Status != InvoiceStatus.Void)
                    .Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct);
                if (existing is not null)
                    throw new BusinessRuleException($"This visit is already invoiced as {existing}. Take the payment against that invoice.");
                dentistId ??= appointment.DentistId;
            }

            var created = await invoices.BuildInvoiceAsync(request.PatientId, dentistId, null,
                [new InvoiceItemRequest(charge.Description, charge.ProcedureId, null, null, 1, charge.Total, 0)], 0, null, null, false, ct);
            if (paidAt < created.IssueDate) created.IssueDate = paidAt;
            created.AppointmentId = charge.AppointmentId;
            if (request.Amount > created.Total)
                throw RequestValidationException.For("amount", $"Amount exceeds the charge ({created.Total:0.000}).");
            // Leaving part of a new charge unpaid creates a debt, which is an invoicing decision.
            if (request.Amount < created.Total && !currentUser.HasPermission(Permissions.InvoicesManage))
                throw new BusinessRuleException(
                    $"The charge comes to {created.Total:0.000} and must be paid in full. Someone who manages invoices can record a part payment.");
            db.Invoices.Add(created);
            targets = [created];
        }
        else if (request.InvoiceId is not null)
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
            if (targets.Count == 0)
                throw new BusinessRuleException("This patient has nothing outstanding. Say what the payment is for, and it will be invoiced with the payment.");
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

            if (await attribution.AttributePaymentAsync(payment, inv, ct) is not null) recovered += apply;

            db.Payments.Add(payment);
            ApplyToInvoice(inv, apply);
            remaining -= apply;
            ids.Add(payment.Id);
            receipts.Add(payment.ReceiptNumber);
        }

        await db.SaveChangesAsync(ct);
        return new RecordPaymentResult(ids, receipts, Money.Round(request.Amount - remaining), recovered);
    }

    /// <summary>Corrects the method, date, reference or notes of an entry. Every change is in the audit log.</summary>
    public async Task UpdateAsync(Guid paymentId, UpdatePaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new NotFoundException("Payment", paymentId);
        var paidAt = request.PaidAt.UtcDateTime;
        if (paidAt > clock.UtcNow.AddMinutes(5)) throw RequestValidationException.For("paidAt", "Payment date cannot be in the future.");

        payment.Method = request.Method;
        payment.PaidAt = paidAt;
        payment.Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        payment.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Cancels a payment that was entered by mistake: wrong patient, wrong amount, entered twice. The invoice goes
    /// back to what it was and the entry stops counting everywhere, but it is kept with its reason and receipt number.
    /// Money that was really received and then given back is a refund, not a cancellation.
    /// </summary>
    public async Task CancelAsync(Guid paymentId, CancelPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                      ?? throw new NotFoundException("Payment", paymentId);
        if (payment.Type != PaymentType.Payment)
            throw new BusinessRuleException("A refund cannot be cancelled. If it was entered by mistake, record the payment again.");
        if (await db.Payments.AnyAsync(p => p.RefundOfPaymentId == paymentId, ct))
            throw new BusinessRuleException("This payment has a refund against it and cannot be cancelled.");

        if (payment.Invoice is { } inv && inv.Status != InvoiceStatus.Void)
        {
            inv.AmountPaid = Money.Round(Math.Max(0, inv.AmountPaid - payment.Amount));
            inv.Balance = Money.Round(inv.Total - inv.AmountPaid);
            inv.Status = inv.AmountPaid <= 0 ? InvoiceStatus.Issued : InvoiceStatus.PartiallyPaid;
        }
        await attribution.ReverseForCancellationAsync(payment, ct);

        payment.CancelReason = request.Reason.Trim();
        payment.CancelledById = currentUser.UserId;
        db.Payments.Remove(payment); // soft delete via ISoftDelete
        await db.SaveChangesAsync(ct);

        var patientName = await db.Patients.Where(p => p.Id == payment.PatientId).Select(p => p.FullName).FirstAsync(ct);
        await notifications.NotifyAsync(new NotifyRequest(
            NotificationType.StaffAction, PriorityLevel.Medium,
            $"Payment cancelled: {payment.ReceiptNumber}, {payment.Amount:0.###} from {patientName}",
            $"{currentUser.FullName}: {payment.CancelReason}", $"/patients/{payment.PatientId}?tab=payments", nameof(Payment), payment.Id,
            RequiredPermission: Permissions.SettingsManage, ExcludeUserId: currentUser.UserId), ct);
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
