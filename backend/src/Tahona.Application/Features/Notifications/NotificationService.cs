using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Notifications;

/// <param name="RequiredPermission">Fan out to every active user whose role grants this permission.</param>
/// <param name="UserIds">Or target specific users.</param>
public record NotifyRequest(
    NotificationType Type,
    PriorityLevel Priority,
    string Title,
    string? Body,
    string? Link,
    string? EntityType,
    Guid? EntityId,
    string? RequiredPermission = null,
    IReadOnlyCollection<Guid>? UserIds = null,
    Guid? ExcludeUserId = null,
    bool Deduplicate = true);

public record NotificationDto(
    Guid Id, NotificationType Type, PriorityLevel Priority, string Title, string? Body, string? Link,
    DateTime CreatedAt, bool IsRead);

public record NotificationListDto(IReadOnlyList<NotificationDto> Items, int UnreadCount);

public class NotificationService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(20);

    public async Task NotifyAsync(NotifyRequest request, CancellationToken ct)
    {
        var users = db.Users.Where(u => u.IsActive);
        List<Guid> recipients;
        if (request.UserIds is { Count: > 0 })
        {
            recipients = await users.Where(u => request.UserIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        }
        else if (request.RequiredPermission is not null)
        {
            var perm = request.RequiredPermission;
            recipients = await users.Where(u => u.Role.Permissions.Contains(perm)).Select(u => u.Id).ToListAsync(ct);
        }
        else return;

        if (request.ExcludeUserId is not null) recipients.Remove(request.ExcludeUserId.Value);
        if (recipients.Count == 0) return;

        var now = clock.UtcNow;
        if (request.Deduplicate && request.EntityId is not null)
        {
            var since = now - DedupeWindow;
            var already = await db.Notifications
                .Where(n => n.Type == request.Type && n.EntityId == request.EntityId && n.CreatedAt >= since && recipients.Contains(n.UserId))
                .Select(n => n.UserId).ToListAsync(ct);
            recipients = recipients.Except(already).ToList();
        }

        foreach (var userId in recipients)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = request.Type,
                Priority = request.Priority,
                Title = Truncate(request.Title, 200),
                Body = request.Body is null ? null : Truncate(request.Body, 1000),
                Link = request.Link,
                EntityType = request.EntityType,
                EntityId = request.EntityId
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);

        var items = await q.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(n => new NotificationDto(n.Id, n.Type, n.Priority, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        return new NotificationListDto(items, unread);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
                ?? throw new NotFoundException("Notification", id);
        n.ReadAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var now = clock.UtcNow;
        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(ct);
        unread.ForEach(n => n.ReadAt = now);
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
