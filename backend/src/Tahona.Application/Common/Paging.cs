using Microsoft.EntityFrameworkCore;

namespace Tahona.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PageQuery
{
    private int _page = 1;
    private int _pageSize = 25;

    public int Page { get => _page; init => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; init => _pageSize = value is < 1 or > 200 ? 25 : value; }
}

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}

public static class Money
{
    /// <summary>JOD has 3 minor-unit digits (fils).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
