using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Clinical;

public class TreatmentPlanService(
    IAppDbContext db,
    ISearchProvider search,
    ISequenceGenerator sequences,
    DentalChartService chart,
    IClock clock)
{
    private static readonly AppointmentStatus[] Upcoming = [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed];

    public async Task<PagedResult<TreatmentPlanListItemDto>> ListAsync(TreatmentPlanQuery query, CancellationToken ct)
    {
        var q = db.TreatmentPlans.AsNoTracking();
        if (query.Status is not null) q = q.Where(t => t.Status == query.Status);
        if (query.PatientId is not null) q = q.Where(t => t.PatientId == query.PatientId);
        if (query.DentistId is not null) q = q.Where(t => t.DentistId == query.DentistId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var patientIds = search.FilterPatients(db.Patients, query.Search.Trim()).Select(p => p.Id);
            var term = query.Search.Trim();
            q = q.Where(t => patientIds.Contains(t.PatientId) || t.PlanNumber.Contains(term) || t.Title.Contains(term));
        }

        var now = clock.UtcNow;
        if (query.Stalled == true)
        {
            q = q.Where(t => (t.Status == TreatmentPlanStatus.Presented || t.Status == TreatmentPlanStatus.Accepted
                              || t.Status == TreatmentPlanStatus.InProgress || t.Status == TreatmentPlanStatus.PartiallyCompleted)
                             && !db.Appointments.Any(a => a.PatientId == t.PatientId && a.StartTime > now && Upcoming.Contains(a.Status)));
        }

        var projected = ProjectList(q.OrderByDescending(t => t.CreatedAt), now);
        var page = await projected.ToPagedAsync(query, ct);
        return new PagedResult<TreatmentPlanListItemDto>(page.Items.Select(Finish).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<TreatmentPlanDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var summary = await ProjectList(db.TreatmentPlans.AsNoTracking().Where(t => t.Id == id), now).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Treatment plan", id);

        var plan = await db.TreatmentPlans.AsNoTracking().Include(t => t.Items).FirstAsync(t => t.Id == id, ct);
        var items = plan.Items.OrderBy(i => i.Sequence).Select(i => new TreatmentPlanItemDto(
            i.Id, i.ProcedureId, i.Name, i.ToothNumber, i.Surfaces, i.Quantity, i.UnitPrice, i.Discount, i.Total,
            i.Status, i.Sequence, i.CompletedAt, i.IsInvoiced)).ToList();

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TreatmentPlanId == id)
            .OrderByDescending(i => i.IssueDate)
            .Select(i => new LinkedInvoiceDto(i.Id, i.InvoiceNumber, i.Total, i.AmountPaid, i.Balance, i.Status, i.IssueDate))
            .ToListAsync(ct);

        var appointments = await db.Appointments.AsNoTracking().Where(a => a.TreatmentPlanId == id)
            .OrderByDescending(a => a.StartTime)
            .Select(a => new LinkedAppointmentDto(a.Id, a.StartTime, a.Treatment, a.Status, a.Dentist.FullName))
            .ToListAsync(ct);

        var subtotal = plan.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => i.Total);
        var uninvoiced = plan.Items.Where(i => i.Status == TreatmentItemStatus.Completed && !i.IsInvoiced).Sum(i => i.Total);

        return new TreatmentPlanDetailDto(Finish(summary), plan.Notes, plan.DiscountAmount, subtotal,
            plan.AcceptedAt, plan.StartedAt, plan.CompletedAt, plan.RejectedAt, plan.RejectionReason,
            items, invoices, appointments, uninvoiced);
    }

    public async Task<Guid> CreateAsync(TreatmentPlanUpsertRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == request.PatientId, ct))
            throw RequestValidationException.For("patientId", "Patient not found.");
        if (!await db.Dentists.AnyAsync(d => d.Id == request.DentistId, ct))
            throw RequestValidationException.For("dentistId", "Dentist not found.");

        var now = clock.UtcNow;
        var plan = new TreatmentPlan
        {
            PlanNumber = await sequences.NextAsync(SequenceKind.TreatmentPlan, ct),
            PatientId = request.PatientId,
            DentistId = request.DentistId,
            Title = request.Title.Trim(),
            Notes = request.Notes?.Trim(),
            DiscountAmount = Money.Round(request.DiscountAmount),
            Status = request.Present ? TreatmentPlanStatus.Presented : TreatmentPlanStatus.Draft,
            PresentedAt = request.Present ? now : null
        };

        await ApplyItemsAsync(plan, request.Items, ct);
        Recalculate(plan);
        db.TreatmentPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return plan.Id;
    }

    public async Task UpdateAsync(Guid id, TreatmentPlanUpsertRequest request, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new NotFoundException("Treatment plan", id);
        if (plan.Status is TreatmentPlanStatus.Completed or TreatmentPlanStatus.Rejected)
            throw new BusinessRuleException("Completed or rejected plans cannot be edited.");
        if (request.PatientId != plan.PatientId)
            throw RequestValidationException.For("patientId", "A plan cannot be moved to another patient.");
        if (!await db.Dentists.AnyAsync(d => d.Id == request.DentistId, ct))
            throw RequestValidationException.For("dentistId", "Dentist not found.");

        plan.DentistId = request.DentistId;
        plan.Title = request.Title.Trim();
        plan.Notes = request.Notes?.Trim();
        plan.DiscountAmount = Money.Round(request.DiscountAmount);
        if (request.Present && plan.Status == TreatmentPlanStatus.Draft)
        {
            plan.Status = TreatmentPlanStatus.Presented;
            plan.PresentedAt = clock.UtcNow;
        }

        await ApplyItemsAsync(plan, request.Items, ct);
        Recalculate(plan);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangeStatusAsync(Guid id, PlanStatusRequest request, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
                   ?? throw new NotFoundException("Treatment plan", id);
        var now = clock.UtcNow;
        var from = plan.Status;
        var to = request.Status;
        if (from == to) return;

        var allowed = from switch
        {
            TreatmentPlanStatus.Draft => to is TreatmentPlanStatus.Presented or TreatmentPlanStatus.Accepted or TreatmentPlanStatus.Rejected,
            TreatmentPlanStatus.Presented => to is TreatmentPlanStatus.Accepted or TreatmentPlanStatus.Rejected or TreatmentPlanStatus.Draft,
            TreatmentPlanStatus.Accepted => to is TreatmentPlanStatus.InProgress or TreatmentPlanStatus.Rejected or TreatmentPlanStatus.Presented,
            TreatmentPlanStatus.InProgress => to is TreatmentPlanStatus.PartiallyCompleted or TreatmentPlanStatus.Completed or TreatmentPlanStatus.Rejected,
            TreatmentPlanStatus.PartiallyCompleted => to is TreatmentPlanStatus.InProgress or TreatmentPlanStatus.Completed or TreatmentPlanStatus.Rejected,
            TreatmentPlanStatus.Rejected => to is TreatmentPlanStatus.Presented or TreatmentPlanStatus.Draft,
            _ => false
        };
        if (!allowed) throw new BusinessRuleException($"A plan cannot move from {from} to {to}.");

        var hasOpenItems = plan.Items.Any(i => i.Status is TreatmentItemStatus.Planned or TreatmentItemStatus.Scheduled);
        if (to == TreatmentPlanStatus.Completed && hasOpenItems)
            throw new BusinessRuleException("Complete or cancel the remaining items first, or mark the plan as Partially Completed.");
        if (to == TreatmentPlanStatus.PartiallyCompleted && !plan.Items.Any(i => i.Status == TreatmentItemStatus.Completed))
            throw new BusinessRuleException("Partially completed requires at least one completed item.");

        plan.Status = to;
        switch (to)
        {
            case TreatmentPlanStatus.Presented: plan.PresentedAt = now; plan.RejectedAt = null; plan.RejectionReason = null; break;
            case TreatmentPlanStatus.Accepted: plan.AcceptedAt = now; plan.PresentedAt ??= now; break;
            case TreatmentPlanStatus.InProgress: plan.StartedAt ??= now; plan.LastActivityAt = now; break;
            case TreatmentPlanStatus.Completed: plan.CompletedAt = now; break;
            case TreatmentPlanStatus.Rejected: plan.RejectedAt = now; plan.RejectionReason = request.Reason?.Trim(); break;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteItemAsync(Guid planId, Guid itemId, CompleteItemRequest request, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.Include(t => t.Items).ThenInclude(i => i.Procedure)
                       .Include(t => t.Patient)
                       .FirstOrDefaultAsync(t => t.Id == planId, ct)
                   ?? throw new NotFoundException("Treatment plan", planId);
        var item = plan.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Plan item", itemId);

        if (plan.Status is TreatmentPlanStatus.Rejected or TreatmentPlanStatus.Draft)
            throw new BusinessRuleException("Present and accept the plan before completing treatment.");
        if (item.Status == TreatmentItemStatus.Completed) throw new BusinessRuleException("This item is already completed.");
        if (item.Status == TreatmentItemStatus.Cancelled) throw new BusinessRuleException("This item was cancelled.");

        var now = clock.UtcNow;
        var performedAt = request.PerformedAt?.UtcDateTime ?? now;
        if (performedAt > now.AddMinutes(5)) throw RequestValidationException.For("performedAt", "Treatment date cannot be in the future.");
        var dentistId = request.DentistId ?? plan.DentistId;

        item.Status = TreatmentItemStatus.Completed;
        item.CompletedAt = performedAt;
        item.AppointmentId = request.AppointmentId ?? item.AppointmentId;

        db.TreatmentRecords.Add(new TreatmentRecord
        {
            PatientId = plan.PatientId,
            DentistId = dentistId,
            AppointmentId = request.AppointmentId,
            TreatmentPlanItemId = item.Id,
            ProcedureId = item.ProcedureId,
            ProcedureName = item.Name,
            Category = item.Procedure?.Category,
            ToothNumber = item.ToothNumber,
            Surfaces = item.Surfaces,
            PerformedAt = performedAt,
            Price = item.Total,
            Notes = request.Notes?.Trim()
        });

        if (item.ToothNumber is not null && item.Procedure?.ResultingCondition is not null)
            await chart.RecordTreatmentResultAsync(plan.PatientId, item.ToothNumber.Value, item.Procedure.ResultingCondition.Value,
                item.Surfaces, dentistId, item.Id, ct);

        plan.LastActivityAt = now;
        plan.AcceptedAt ??= now;
        plan.StartedAt ??= performedAt;
        var remaining = plan.Items.Any(i => i.Status is TreatmentItemStatus.Planned or TreatmentItemStatus.Scheduled);
        plan.Status = remaining ? TreatmentPlanStatus.InProgress : TreatmentPlanStatus.Completed;
        if (!remaining) plan.CompletedAt = now;

        if (plan.Patient.LastVisitAt is null || plan.Patient.LastVisitAt < performedAt) plan.Patient.LastVisitAt = performedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelItemAsync(Guid planId, Guid itemId, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == planId, ct)
                   ?? throw new NotFoundException("Treatment plan", planId);
        var item = plan.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Plan item", itemId);
        if (item.Status == TreatmentItemStatus.Completed) throw new BusinessRuleException("A completed item cannot be cancelled.");

        item.Status = TreatmentItemStatus.Cancelled;
        Recalculate(plan);
        var active = plan.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).ToList();
        if (active.Count > 0 && active.All(i => i.Status == TreatmentItemStatus.Completed)
            && plan.Status is TreatmentPlanStatus.InProgress or TreatmentPlanStatus.PartiallyCompleted)
        {
            plan.Status = TreatmentPlanStatus.Completed;
            plan.CompletedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task ApplyItemsAsync(TreatmentPlan plan, IReadOnlyList<TreatmentPlanItemRequest> requests, CancellationToken ct)
    {
        var procedureIds = requests.Where(r => r.ProcedureId is not null).Select(r => r.ProcedureId!.Value).Distinct().ToList();
        var procedures = await db.Procedures.Where(p => procedureIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (procedures.Count != procedureIds.Count) throw RequestValidationException.For("items", "One or more procedures were not found.");

        var keepIds = requests.Where(r => r.Id is not null).Select(r => r.Id!.Value).ToHashSet();
        foreach (var removed in plan.Items.Where(i => !keepIds.Contains(i.Id) && i.Status != TreatmentItemStatus.Completed).ToList())
        {
            if (removed.IsInvoiced) removed.Status = TreatmentItemStatus.Cancelled;
            else
            {
                plan.Items.Remove(removed);
                db.TreatmentPlanItems.Remove(removed);
            }
        }

        var sequence = 1;
        foreach (var r in requests)
        {
            var procedure = r.ProcedureId is null ? null : procedures[r.ProcedureId.Value];
            var item = r.Id is null ? null : plan.Items.FirstOrDefault(i => i.Id == r.Id);
            if (r.Id is not null && item is null) throw RequestValidationException.For("items", "Plan item not found.");

            if (item is null)
            {
                item = new TreatmentPlanItem { TreatmentPlanId = plan.Id };
                plan.Items.Add(item);
            }
            else if (item.Status == TreatmentItemStatus.Completed)
            {
                item.Sequence = sequence++;
                continue; // completed items are immutable
            }

            item.ProcedureId = procedure?.Id;
            item.Name = string.IsNullOrWhiteSpace(r.Name) ? procedure!.Name : r.Name.Trim();
            item.ToothNumber = r.ToothNumber;
            item.Surfaces = r.Surfaces?.ToUpperInvariant();
            item.Quantity = r.Quantity;
            item.UnitPrice = Money.Round(r.UnitPrice ?? procedure?.DefaultPrice ?? 0);
            item.Discount = Money.Round(r.Discount);
            item.Total = Money.Round(Math.Max(0, item.UnitPrice * item.Quantity - item.Discount));
            item.Sequence = sequence++;
        }
    }

    private static void Recalculate(TreatmentPlan plan)
    {
        var subtotal = plan.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => i.Total);
        if (plan.DiscountAmount > subtotal) plan.DiscountAmount = subtotal;
        plan.TotalAmount = Money.Round(subtotal - plan.DiscountAmount);
    }

    private IQueryable<PlanRow> ProjectList(IQueryable<TreatmentPlan> q, DateTime now) => q.Select(t => new PlanRow
    {
        Id = t.Id, PlanNumber = t.PlanNumber, PatientId = t.PatientId, PatientName = t.Patient.FullName,
        PatientNameAr = t.Patient.FullNameAr, PatientPhone = t.Patient.Phone, DentistId = t.DentistId,
        DentistName = t.Dentist.FullName, Title = t.Title, Status = t.Status, TotalAmount = t.TotalAmount,
        DiscountAmount = t.DiscountAmount,
        Paid = db.Invoices.Where(i => i.TreatmentPlanId == t.Id && i.Status != InvoiceStatus.Void).Sum(i => (decimal?)i.AmountPaid) ?? 0,
        ActiveItemsTotal = t.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => (decimal?)i.Total) ?? 0,
        CompletedValue = t.Items.Where(i => i.Status == TreatmentItemStatus.Completed).Sum(i => (decimal?)i.Total) ?? 0,
        ItemCount = t.Items.Count(i => i.Status != TreatmentItemStatus.Cancelled),
        CompletedItems = t.Items.Count(i => i.Status == TreatmentItemStatus.Completed),
        CreatedAt = t.CreatedAt, PresentedAt = t.PresentedAt, LastActivityAt = t.LastActivityAt,
        NextAppointmentAt = db.Appointments.Where(a => a.PatientId == t.PatientId && a.StartTime > now && Upcoming.Contains(a.Status))
            .Min(a => (DateTime?)a.StartTime),
        Now = now
    });

    private static TreatmentPlanListItemDto Finish(PlanRow r)
    {
        var percent = r.ActiveItemsTotal == 0
            ? (r.ItemCount == 0 ? 0 : (int)Math.Round(100.0 * r.CompletedItems / r.ItemCount))
            : (int)Math.Round(100 * (double)(r.CompletedValue / r.ActiveItemsTotal));
        var lastActivity = r.LastActivityAt ?? r.PresentedAt ?? r.CreatedAt;
        return new TreatmentPlanListItemDto(
            r.Id, r.PlanNumber, r.PatientId, r.PatientName, r.PatientNameAr, r.PatientPhone, r.DentistId, r.DentistName, r.Title,
            r.Status, r.TotalAmount, r.Paid, Math.Max(0, r.TotalAmount - r.Paid), r.CompletedValue, percent,
            r.ItemCount, r.CompletedItems, r.CreatedAt, r.PresentedAt, r.LastActivityAt, r.NextAppointmentAt,
            Math.Max(0, (int)(r.Now - lastActivity).TotalDays));
    }

    private sealed class PlanRow
    {
        public Guid Id { get; init; }
        public string PlanNumber { get; init; } = default!;
        public Guid PatientId { get; init; }
        public string PatientName { get; init; } = default!;
        public string? PatientNameAr { get; init; }
        public string PatientPhone { get; init; } = default!;
        public Guid DentistId { get; init; }
        public string DentistName { get; init; } = default!;
        public string Title { get; init; } = default!;
        public TreatmentPlanStatus Status { get; init; }
        public decimal TotalAmount { get; init; }
        public decimal DiscountAmount { get; init; }
        public decimal Paid { get; init; }
        public decimal ActiveItemsTotal { get; init; }
        public decimal CompletedValue { get; init; }
        public int ItemCount { get; init; }
        public int CompletedItems { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? PresentedAt { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public DateTime? NextAppointmentAt { get; init; }
        public DateTime Now { get; init; }
    }
}
