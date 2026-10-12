using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Infrastructure.Ai;
using Tahona.Infrastructure.Auth;
using Tahona.Infrastructure.BackgroundJobs;
using Tahona.Infrastructure.Messaging;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Persistence.Seed;
using Tahona.Infrastructure.Services;

namespace Tahona.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Connection string 'Default' is missing. Set ConnectionStrings__Default (see .env.example).");

        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connection, npg => npg.EnableRetryOnFailure(3).MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddMemoryCache();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IClinicTime, ClinicTime>();
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();
        services.AddScoped<ISearchProvider, SearchProvider>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // AI: Gemini by default (free-tier friendly); set Ai:Provider=claude to use Anthropic instead.
        services.Configure<AiOptions>(config.GetSection(AiOptions.Section));
        services.AddSingleton<ClaudeAiProvider>();
        services.AddHttpClient<GeminiAiProvider>(c => c.Timeout = TimeSpan.FromSeconds(90));
        services.AddScoped<IAiProvider>(sp =>
            sp.GetRequiredService<IOptions<AiOptions>>().Value.UseClaude
                ? sp.GetRequiredService<ClaudeAiProvider>()
                : sp.GetRequiredService<GeminiAiProvider>());

        // WhatsApp: real Cloud API when credentials are present, otherwise wa.me manual sending.
        services.Configure<WhatsAppOptions>(config.GetSection(WhatsAppOptions.Section));
        services.AddHttpClient<MetaCloudWhatsAppProvider>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<IWhatsAppProvider>(sp =>
            sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value.HasCloudApiCredentials
                ? sp.GetRequiredService<MetaCloudWhatsAppProvider>()
                : new ManualWhatsAppProvider());

        services.Configure<AutomationOptions>(config.GetSection(AutomationOptions.Section));
        services.AddSingleton<RecoveryAutomationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<RecoveryAutomationWorker>());

        services.AddScoped<DemoDataSeeder>();
        return services;
    }
}
