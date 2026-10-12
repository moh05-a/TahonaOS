using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Clinical;

public record ProcedureDto(
    Guid Id, string Code, string Name, string? NameAr, ProcedureCategory Category, decimal DefaultPrice,
    int DefaultDurationMinutes, ToothConditionType? ResultingCondition, bool IsActive);

public record ProcedureUpsertRequest(
    string Code, string Name, string? NameAr, ProcedureCategory Category, decimal DefaultPrice,
    int DefaultDurationMinutes, ToothConditionType? ResultingCondition, bool IsActive);

public class ProcedureUpsertValidator : AbstractValidator<ProcedureUpsertRequest>
{
    public ProcedureUpsertValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameAr).MaximumLength(150);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.DefaultPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000);
        RuleFor(x => x.DefaultDurationMinutes).InclusiveBetween(5, 480);
    }
}

public class ProcedureService(IAppDbContext db)
{
    public async Task<IReadOnlyList<ProcedureDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Procedures.AsNoTracking();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        return await q.OrderBy(p => p.Category).ThenBy(p => p.Name)
            .Select(p => new ProcedureDto(p.Id, p.Code, p.Name, p.NameAr, p.Category, p.DefaultPrice,
                p.DefaultDurationMinutes, p.ResultingCondition, p.IsActive))
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(ProcedureUpsertRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Procedures.AnyAsync(p => p.Code == code, ct)) throw RequestValidationException.For("code", "Code already exists.");
        var p = new Procedure();
        Apply(p, r with { Code = code });
        db.Procedures.Add(p);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task UpdateAsync(Guid id, ProcedureUpsertRequest r, CancellationToken ct)
    {
        var p = await db.Procedures.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Procedure", id);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Procedures.AnyAsync(x => x.Code == code && x.Id != id, ct)) throw RequestValidationException.For("code", "Code already exists.");
        Apply(p, r with { Code = code });
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Procedure p, ProcedureUpsertRequest r)
    {
        p.Code = r.Code;
        p.Name = r.Name.Trim();
        p.NameAr = r.NameAr?.Trim();
        p.Category = r.Category;
        p.DefaultPrice = Money.Round(r.DefaultPrice);
        p.DefaultDurationMinutes = r.DefaultDurationMinutes;
        p.ResultingCondition = r.ResultingCondition;
        p.IsActive = r.IsActive;
    }
}
