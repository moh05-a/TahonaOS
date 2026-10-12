using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Application.Features.Recovery;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Infrastructure.BackgroundJobs;

public class AutomationOptions
{
    public const string Section = "Automation";
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
    public int StartupDelaySeconds { get; set; } = 10;
}

/// <summary>
/// Runs the Follow-Up Automation Engine for every active clinic on a fixed interval, each in its own scope with the
/// tenant pinned explicitly. One failing clinic never blocks the others.
/// </summary>
public sealed class RecoveryAutomationWorker(IServiceScopeFactory scopes, IOptions<AutomationOptions> options, ILogger<RecoveryAutomationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Recovery automation disabled by configuration.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(opts.StartupDelaySeconds), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, opts.IntervalMinutes)));
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> clinicIds;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            clinicIds = await db.Clinics.IgnoreQueryFilters().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        }

        foreach (var clinicId in clinicIds)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(clinicId);
                var result = await scope.ServiceProvider.GetRequiredService<RecoverySyncService>().SyncAsync(ct);
                logger.LogInformation(
                    "Recovery sync for clinic {ClinicId}: {Created} new follow-ups, {Resolved} resolved, {Booked} booked, {Auto} auto-messages, {Reminders} reminders",
                    clinicId, result.Created, result.Resolved, result.Booked, result.AutoMessages, result.Reminders);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Recovery sync failed for clinic {ClinicId}", clinicId);
            }
        }
    }
}
