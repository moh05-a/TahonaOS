using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Billing;

public class InvoiceService(
    IAppDbContext db, ISearchProvider search, ISequenceGenerator sequences, IClock clock,
    NotificationService notifications, ICurrentUser currentUser)
{
    private static readonly InvoiceStatus[] OpenStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid];

    public async Task<PagedResult<InvoiceListItemDto>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Invoices.AsNoTracking();
        if (query.Status is not null) q = q.Where(i => i.Status == query.Status);
        if (query.PatientId is not null) q = q.Where(i => i.PatientId == query.PatientId);
        if (query.OpenOnly == true) q = q.Where(i => OpenStatuses.Contains(i.Status) && i.Balance > 0);
        if (query.OverdueOnly == true) q = q.Where(i => OpenStatuses.Contains(i.Status) && i.Balance > 0 && i.DueDate < now);
        if (query.From is not null) q = q.Where(i => i.IssueDate >= query.From.Value.UtcDateTime);
        if (query.To is not null) q = q.Where(i => i.IssueDate < query.To.Value.UtcDateTime);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var patientIds = search.FilterPatients(db.Patients, term).Select(p => p.Id);
            q = q.Where(i => patientIds.Contains(i.PatientId) || i.InvoiceNumber.Contains(term.ToUpper()));
        }

        var page = await Project(q.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber), now).ToPagedAsync(query, ct);
        return page with { Items = page.Items.Select(i => WithDaysOverdue(i, now)).ToList() };
    }

    public async Task<InvoiceDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var summary = await Project(db.Invoices.AsNoTracking().Where(i => i.Id == id), now).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Invoice", id);
        summary = WithDaysOverdue(summary, now);
        var inv = await db.Invoices.AsNoTracking()
            .Include(i => i.Items).Include(i => i.Dentist)
            .Include(i => i.Payments).ThenInclude(p => p.ReceivedBy)
            .FirstAsync(i => i.Id == id, ct);

        return new InvoiceDetailDto(summary, inv.Dentist?.FullName, inv.Subtotal, inv.DiscountAmount, inv.TaxAmount, inv.Notes,
            inv.Items.Select(x => new InvoiceItemDto(x.Id, x.Description, x.ProcedureId, x.TreatmentPlanItemId, x.ToothNumber,
                x.Quantity, x.UnitPrice, x.Discount, x.Total)).ToList(),
            inv.Payments.OrderByDescending(p => p.PaidAt).Select(p => new InvoicePaymentDto(p.Id, p.ReceiptNumber, p.Type, p.Amount,
                p.Method, p.PaidAt, p.Reference, p.ReceivedBy?.FullName, p.RecoveredFollowUpId != null)).ToList());
    }

    public async Task<Guid> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            throw RequestValidationException.For("patientId", "Patient not found.");
        if (request.DentistId is not null && !await db.Dentists.AnyAsync(d => d.Id == request.DentistId, ct))
            throw RequestValidationException.For("dentistId", "Dentist not found.");

        List<TreatmentPlanItem> planItems = [];
        if (request.TreatmentPlanId is not null)
        {
            var plan = await db.TreatmentPlans.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == request.TreatmentPlanId, ct)
                       ?? throw RequestValidationException.For("treatmentPlanId", "Treatment plan not found.");
            if (plan.PatientId != request.PatientId)
                throw RequestValidationException.For("treatmentPlanId", "Treatment plan belongs to another patient.");
            var linkedIds = request.Items.Where(i => i.TreatmentPlanItemId is not null).Select(i => i.TreatmentPlanItemId!.Value).ToHashSet();
            planItems = plan.Items.Where(i => linkedIds.Contains(i.Id)).ToList();
            if (planItems.Count != linkedIds.Count) throw RequestValidationException.For("items", "Plan item not found.");
            if (planItems.Any(i => i.IsInvoiced)) throw new BusinessRuleException("One or more plan items are already invoiced.");
        }

        var invoice = await BuildInvoiceAsync(request.PatientId, request.DentistId, request.TreatmentPlanId, request.Items,
            request.DiscountAmount, request.DueDate, request.Notes, request.SaveAsDraft, ct);
        planItems.ForEach(i => i.IsInvoiced = true);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice.Id;
    }

    /// <summary>Bills completed (and optionally planned) treatment-plan items that are not yet invoiced.</summary>
    public async Task<Guid> CreateFromPlanAsync(Guid planId, InvoiceFromPlanRequest request, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == planId, ct)
                   ?? throw new NotFoundException("Treatment plan", planId);

        var candidates = plan.Items.Where(i => !i.IsInvoiced && i.Status != TreatmentItemStatus.Cancelled
                                               && (request.IncludePlannedItems || i.Status == TreatmentItemStatus.Completed));
        if (request.ItemIds is { Count: > 0 }) candidates = candidates.Where(i => request.ItemIds.Contains(i.Id));
        var items = candidates.OrderBy(i => i.Sequence).ToList();
        if (items.Count == 0) throw new BusinessRuleException("There are no uninvoiced items to bill on this plan.");

        // Spread the plan-level discount proportionally across billed items.
        var planSubtotal = plan.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => i.Total);
        var billedSubtotal = items.Sum(i => i.Total);
        var discount = planSubtotal == 0 ? 0 : Money.Round(plan.DiscountAmount * billedSubtotal / planSubtotal);

        var lines = items.Select(i => new InvoiceItemRequest(
            i.ToothNumber is null ? i.Name : $"{i.Name} (tooth {i.ToothNumber})",
            i.ProcedureId, i.Id, i.ToothNumber, i.Quantity, i.UnitPrice, i.Discount)).ToList();

        var invoice = await BuildInvoiceAsync(plan.PatientId, plan.DentistId, plan.Id, lines, discount, request.DueDate,
            $"Treatment plan {plan.PlanNumber}", false, ct);
        items.ForEach(i => i.IsInvoiced = true);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return invoice.Id;
    }

    public async Task IssueAsync(Guid id, CancellationToken ct)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);
        if (inv.Status != InvoiceStatus.Draft) throw new BusinessRuleException("Only draft invoices can be issued.");
        var now = clock.UtcNow;
        var termDays = (inv.DueDate - inv.IssueDate).Days;
        inv.IssueDate = now;
        inv.DueDate = now.AddDays(Math.Max(0, termDays));
        inv.Status = InvoiceStatus.Issued;
        await db.SaveChangesAsync(ct);
    }

    public async Task VoidAsync(Guid id, VoidInvoiceRequest request, CancellationToken ct)
    {
        var inv = await db.Invoices.Include(i => i.Items).Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, ct)
                  ?? throw new NotFoundException("Invoice", id);
        if (inv.Status == InvoiceStatus.Void) return;
        var net = inv.Payments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount)
                  - inv.Payments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        if (net > 0) throw new BusinessRuleException("Refund the payments on this invoice before voiding it.");

        inv.Status = InvoiceStatus.Void;
        inv.Balance = 0;
        inv.Notes = string.IsNullOrWhiteSpace(inv.Notes) ? $"VOID: {request.Reason}" : $"{inv.Notes}\nVOID: {request.Reason}";

        var planItemIds = inv.Items.Where(i => i.TreatmentPlanItemId is not null).Select(i => i.TreatmentPlanItemId!.Value).ToList();
        if (planItemIds.Count > 0)
        {
            var planItems = await db.TreatmentPlanItems.Where(i => planItemIds.Contains(i.Id)).ToListAsync(ct);
            planItems.ForEach(i => i.IsInvoiced = false);
        }
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(new NotifyRequest(
            NotificationType.StaffAction, PriorityLevel.Medium,
            $"Invoice {inv.InvoiceNumber} voided", $"{currentUser.FullName}: {request.Reason}",
            $"/invoices/{inv.Id}", nameof(Invoice), inv.Id,
            RequiredPermission: Permissions.SettingsManage, ExcludeUserId: currentUser.UserId), ct);
    }

    public async Task<IReadOnlyList<PatientBalanceDto>> OutstandingBalancesAsync(int take, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.Invoices.AsNoTracking()
            .Where(i => OpenStatuses.Contains(i.Status) && i.Balance > 0)
            .GroupBy(i => new { i.PatientId, i.Patient.FullName, i.Patient.FullNameAr, i.Patient.Phone })
            .Select(g => new
            {
                g.Key.PatientId, g.Key.FullName, g.Key.FullNameAr, g.Key.Phone,
                Balance = g.Sum(i => i.Balance),
                Count = g.Count(),
                OldestDue = g.Min(i => i.DueDate)
            })
            .OrderByDescending(r => r.Balance)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        var ids = rows.Select(r => r.PatientId).ToList();
        var lastPayments = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.PatientId) && p.Type == PaymentType.Payment)
            .GroupBy(p => p.PatientId).Select(g => new { PatientId = g.Key, Last = g.Max(p => p.PaidAt) })
            .ToDictionaryAsync(x => x.PatientId, x => x.Last, ct);

        return rows.Select(r => new PatientBalanceDto(r.PatientId, r.FullName, r.FullNameAr, r.Phone, r.Balance, r.Count, r.OldestDue,
            Math.Max(0, (int)(now - r.OldestDue).TotalDays), lastPayments.TryGetValue(r.PatientId, out var lp) ? lp : null)).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Invoice> BuildInvoiceAsync(Guid patientId, Guid? dentistId, Guid? planId, IReadOnlyList<InvoiceItemRequest> lines,
        decimal discount, DateTimeOffset? dueDate, string? notes, bool draft, CancellationToken ct)
    {
        var settings = await db.ClinicSettings.AsNoTracking().FirstAsync(ct);
        var now = clock.UtcNow;
        var due = dueDate?.UtcDateTime ?? now.AddDays(settings.InvoiceDueDays);
        if (due < now.Date) throw RequestValidationException.For("dueDate", "Due date cannot be in the past.");

        var invoice = new Invoice
        {
            InvoiceNumber = await sequences.NextAsync(SequenceKind.Invoice, ct),
            PatientId = patientId,
            DentistId = dentistId,
            TreatmentPlanId = planId,
            IssueDate = now,
            DueDate = due,
            Status = draft ? InvoiceStatus.Draft : InvoiceStatus.Issued,
            Notes = notes?.Trim()
        };

        foreach (var l in lines)
        {
            invoice.Items.Add(new InvoiceItem
            {
                InvoiceId = invoice.Id,
                Description = l.Description.Trim(),
                ProcedureId = l.ProcedureId,
                TreatmentPlanItemId = l.TreatmentPlanItemId,
                ToothNumber = l.ToothNumber,
                Quantity = l.Quantity,
                UnitPrice = Money.Round(l.UnitPrice),
                Discount = Money.Round(l.Discount),
                Total = Money.Round(l.UnitPrice * l.Quantity - l.Discount)
            });
        }

        invoice.Subtotal = invoice.Items.Sum(i => i.Total);
        invoice.DiscountAmount = Money.Round(Math.Min(discount, invoice.Subtotal));
        var taxable = invoice.Subtotal - invoice.DiscountAmount;
        invoice.TaxAmount = Money.Round(taxable * settings.TaxRatePercent / 100m);
        invoice.Total = Money.Round(taxable + invoice.TaxAmount);
        invoice.AmountPaid = 0;
        invoice.Balance = invoice.Total;
        if (invoice.Total == 0 && !draft) invoice.Status = InvoiceStatus.Paid;
        return invoice;
    }

    private static InvoiceListItemDto WithDaysOverdue(InvoiceListItemDto i, DateTime now) =>
        i.IsOverdue ? i with { DaysOverdue = Math.Max(1, (int)(now - i.DueDate).TotalDays) } : i;

    private static IQueryable<InvoiceListItemDto> Project(IQueryable<Invoice> q, DateTime now) => q.Select(i => new InvoiceListItemDto(
        i.Id, i.InvoiceNumber, i.PatientId, i.Patient.FullName, i.Patient.FullNameAr, i.Patient.Phone,
        i.IssueDate, i.DueDate, i.Total, i.AmountPaid, i.Balance, i.Status,
        (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0 && i.DueDate < now,
        0,
        i.TreatmentPlan != null ? i.TreatmentPlan.PlanNumber : null, i.TreatmentPlanId));
}
