using System.Diagnostics;
using Tahona.Application.Common;

namespace Tahona.Api.Infrastructure;

/// <summary>
/// One log line per API request: method, route, status, duration, clinic and user. The query string and the body are
/// never logged, because they carry search terms, tokens and patient data. Server errors are logged as warnings so an
/// alert can be set on them.
/// </summary>
public sealed class RequestLogMiddleware(RequestDelegate next, ILogger<RequestLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUser user)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var status = context.Response.StatusCode;
            // The route template ("/api/patients/{id}") groups requests without recording which record was opened.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
            logger.Log(status >= 500 ? LogLevel.Warning : LogLevel.Information,
                "HTTP {Method} {Route} {Status} {ElapsedMs}ms clinic={ClinicId} user={UserId}",
                context.Request.Method, route, status, elapsed, user.ClinicId, user.UserId);
        }
    }
}
