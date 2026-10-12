using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;

namespace Tahona.Application.Features.Settings;

public record AuditLogQuery : PageQuery
{
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? UserId { get; init; }
    public string? Action { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public record AuditLogDto(Guid Id, DateTime Timestamp, Guid? UserId, string? UserName, string Action, string EntityType,
    Guid? EntityId, string? Changes, string? IpAddress);

public class AuditLogService(IAppDbContext db)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (query.EntityId is not null) q = q.Where(a => a.EntityId == query.EntityId);
        if (query.UserId is not null) q = q.Where(a => a.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action == query.Action);
        if (query.From is not null) q = q.Where(a => a.Timestamp >= query.From.Value.UtcDateTime);
        if (query.To is not null) q = q.Where(a => a.Timestamp < query.To.Value.UtcDateTime);

        return await q.OrderByDescending(a => a.Timestamp)
            .Select(a => new AuditLogDto(a.Id, a.Timestamp, a.UserId, a.UserName, a.Action, a.EntityType, a.EntityId, a.Changes, a.IpAddress))
            .ToPagedAsync(query, ct);
    }

    public async Task<IReadOnlyList<string>> EntityTypesAsync(CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking().Select(a => a.EntityType).Distinct().OrderBy(x => x).ToListAsync(ct);
}
