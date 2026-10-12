using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Billing;

public class ExpenseService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<PagedResult<ExpenseDto>> ListAsync(ExpenseQuery query, CancellationToken ct)
    {
        var q = db.Expenses.AsNoTracking();
        if (query.Category is not null) q = q.Where(e => e.Category == query.Category);
        if (query.From is not null) q = q.Where(e => e.ExpenseDate >= query.From.Value.UtcDateTime);
        if (query.To is not null) q = q.Where(e => e.ExpenseDate < query.To.Value.UtcDateTime);

        return await q.OrderByDescending(e => e.ExpenseDate)
            .Select(e => new ExpenseDto(e.Id, e.Category, e.Description, e.Amount, e.ExpenseDate, e.Vendor, e.PaymentMethod, e.Reference))
            .ToPagedAsync(query, ct);
    }

    public async Task<Guid> CreateAsync(ExpenseRequest request, CancellationToken ct)
    {
        Validate(request);
        var e = new Expense { RecordedById = currentUser.UserId };
        Apply(e, request);
        db.Expenses.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task UpdateAsync(Guid id, ExpenseRequest request, CancellationToken ct)
    {
        Validate(request);
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense", id);
        Apply(e, request);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Expense", id);
        db.Expenses.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    private void Validate(ExpenseRequest r)
    {
        if (r.ExpenseDate.UtcDateTime > clock.UtcNow.AddDays(1))
            throw RequestValidationException.For("expenseDate", "Expense date cannot be in the future.");
    }

    private static void Apply(Expense e, ExpenseRequest r)
    {
        e.Category = r.Category;
        e.Description = r.Description.Trim();
        e.Amount = Money.Round(r.Amount);
        e.ExpenseDate = r.ExpenseDate.UtcDateTime;
        e.Vendor = r.Vendor?.Trim();
        e.PaymentMethod = r.PaymentMethod;
        e.Reference = r.Reference?.Trim();
    }
}
