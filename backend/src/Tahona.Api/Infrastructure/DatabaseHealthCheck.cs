using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Api.Infrastructure;

/// <summary>Readiness probe: the API is only ready when PostgreSQL answers.</summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is not reachable.");
}
