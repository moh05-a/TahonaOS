using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tahona.Application.Common;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Api.Infrastructure;

/// <summary>
/// Enforces the subscription policy on the server. A cancelled or suspended clinic stays signed in and can read and
/// export everything, but any request that would change data is refused. An archived clinic is signed out.
/// The state is cached briefly, so an operator's change takes effect within <see cref="CacheFor"/>.
/// </summary>
public sealed class ClinicAccessMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    /// <summary>Changes that remain possible while read-only: signing in and out, and one's own account settings.</summary>
    private static readonly string[] AlwaysAllowed = ["/api/auth/", "/api/export/"];

    public async Task InvokeAsync(HttpContext context, ICurrentUser user, AppDbContext db)
    {
        if (user.ClinicId is not { } clinicId || HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            || AlwaysAllowed.Any(p => context.Request.Path.StartsWithSegments(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var state = await cache.GetOrCreateAsync($"clinic-access:{clinicId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await db.Clinics.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == clinicId)
                .Select(c => new ClinicState(c.IsActive, c.SubscriptionStatus)).FirstOrDefaultAsync(context.RequestAborted);
        });

        if (state is null || !state.IsActive)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, "This clinic account is not active.");
            return;
        }
        if (ClinicAccess.IsReadOnly(state.Status))
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden,
                "This account is read-only because the subscription is not active. You can still view and export all records. Contact support to reactivate it.");
            return;
        }
        await next(context);
    }

    private static Task WriteAsync(HttpContext context, int status, string title)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title },
            options: null, contentType: "application/problem+json");
    }

    private sealed record ClinicState(bool IsActive, SubscriptionStatus Status);
}
