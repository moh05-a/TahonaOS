using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Auth;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Services;

namespace Tahona.Tests;

public sealed class MutableClock(DateTime now) : IClock
{
    public DateTime UtcNow { get; set; } = now;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>The caller of the current test step. Switch clinic or user with <see cref="TestApp.ActAs"/>.</summary>
public sealed class TestUser : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;
    public Guid? UserId { get; set; }
    public Guid? ClinicId { get; set; }
    public string? FullName { get; set; } = "Test User";
    public string? Role { get; set; } = SystemRoles.Admin;
    public HashSet<string> Granted { get; } = [.. Tahona.Domain.Authorization.Permissions.All];
    public IReadOnlyCollection<string> Permissions => Granted;
    public string? IpAddress => "127.0.0.1";
    public bool HasPermission(string permission) => Granted.Contains(permission);
}

/// <summary>Records what would have been sent, and answers as scripted.</summary>
public sealed class FakeWhatsApp : IWhatsAppProvider
{
    public string Name => "fake";
    public bool IsConfigured { get; set; } = true;
    public bool Fail { get; set; }
    public List<WhatsAppSendRequest> Sent { get; } = [];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken ct)
    {
        Sent.Add(request);
        return Task.FromResult(Fail
            ? new WhatsAppSendResult(false, null, "provider down", false, null)
            : new WhatsAppSendResult(true, $"wamid.{Guid.NewGuid():N}", null, false, null));
    }
}

/// <summary>An "LLM" that calls the tools it is told to and exposes exactly what the provider would have received.</summary>
public sealed class FakeAi : IAiProvider
{
    public string ProviderName => "Fake";
    public bool IsConfigured { get; set; } = true;
    public string ModelName => "fake-1";
    public List<string> ToolsToCall { get; } = [];
    public List<string> OfferedTools { get; private set; } = [];
    public List<string> ToolResults { get; } = [];
    public string? LastSystemPrompt { get; private set; }
    public string? LastJsonInput { get; private set; }

    public async Task<AiRunResult> RunWithToolsAsync(string system, IReadOnlyList<AiChatTurn> conversation, IReadOnlyList<AiTool> tools,
        int maxIterations, CancellationToken ct)
    {
        LastSystemPrompt = system;
        OfferedTools = tools.Select(t => t.Name).ToList();
        var traces = new List<AiToolTrace>();
        foreach (var name in ToolsToCall)
        {
            var tool = tools.FirstOrDefault(t => t.Name == name);
            if (tool is null) continue;
            ToolResults.Add(await tool.ExecuteAsync(JsonDocument.Parse("{}").RootElement, ct));
            traces.Add(new AiToolTrace(name, "{}", false));
        }
        return new AiRunResult("done", traces, false);
    }

    public Task<string?> CompleteJsonAsync(string system, string userText, JsonElement schema, CancellationToken ct)
    {
        LastJsonInput = userText;
        return Task.FromResult<string?>("""{"chief_complaint":"pain"}""");
    }
}

public sealed class FixedAiPolicy : IAiDataPolicy
{
    public AiPatientDataMode PatientData { get; set; } = AiPatientDataMode.Minimized;
}

/// <summary>Collects the e-mail the system would have sent.</summary>
public sealed class FakeEmail : IEmailSender
{
    public bool IsConfigured { get; set; }
    public bool Fail { get; set; }
    public List<EmailMessage> Sent { get; } = [];

    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Fail) return Task.FromResult(false);
        Sent.Add(message);
        return Task.FromResult(true);
    }
}

public sealed class FixedPlatformInfo : IPlatformInfo
{
    public string PublicUrl => "https://app.example.test";
    public string? SupportContact { get; set; } = "support@example.test";
    public string? PaymentInstructions { get; set; } = "Pay by CliQ to TAHONA";
}

/// <summary>Answers a WhatsApp credential check as scripted.</summary>
public sealed class FakeWhatsAppVerifier : IWhatsAppAccountVerifier
{
    public string? Error { get; set; }
    public List<(string PhoneNumberId, string Token)> Checked { get; } = [];

    public Task<(WhatsAppNumberInfo? Number, string? Error)> VerifyAsync(string phoneNumberId, string accessToken, CancellationToken ct)
    {
        Checked.Add((phoneNumberId, accessToken));
        return Task.FromResult<(WhatsAppNumberInfo?, string?)>(Error is null
            ? (new WhatsAppNumberInfo(phoneNumberId, "+962 6 555 1234", "Test Clinic"), null)
            : (null, Error));
    }
}

public sealed class MemoryFileStorage : IFileStorage
{
    public string Name => "Memory";

    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task<string> SaveAsync(Guid clinicId, Guid patientId, string fileName, Stream content, CancellationToken ct)
    {
        var path = $"{clinicId:N}/{patientId:N}/{Guid.NewGuid():N}_{fileName}";
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Files[path] = buffer.ToArray();
        return path;
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        Files.Remove(storagePath);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real application services over an in-memory database, with only the outside world faked
/// (WhatsApp, the AI provider, file storage, the clock and the caller).
/// </summary>
public sealed class TestApp : IDisposable
{
    public static readonly DateTime Start = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc); // noon in Amman

    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;

    public MutableClock Clock { get; } = new(Start);
    public TestUser User { get; } = new();
    public FakeWhatsApp WhatsApp { get; } = new();
    public FakeAi Ai { get; } = new() { IsConfigured = false };
    public FixedAiPolicy AiPolicy { get; } = new();
    public MemoryFileStorage Files { get; } = new();
    public FakeEmail Email { get; } = new();
    public FixedPlatformInfo Platform { get; } = new();
    public FakeWhatsAppVerifier WhatsAppVerifier { get; } = new();

    /// <summary>Connection string of a PostgreSQL server for integration tests, or null to skip them.</summary>
    public static string? PostgresServer => Environment.GetEnvironmentVariable("TAHONA_TEST_POSTGRES");

    private readonly bool _postgres;

    /// <param name="postgres">
    /// True to run against a throw-away PostgreSQL database (created and migrated here, dropped on dispose) configured
    /// exactly like production. Needed for anything that depends on real transactions, locks or constraints.
    /// </param>
    /// <param name="configure">Replace or add services, e.g. to inject a failure.</param>
    public TestApp(bool postgres = false, Action<IServiceCollection>? configure = null)
    {
        _postgres = postgres;
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddApplication();
        if (postgres)
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(PostgresServer
                ?? throw new InvalidOperationException("Set TAHONA_TEST_POSTGRES to run PostgreSQL tests.")) { Database = $"tahona_it_{dbName}" };
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection.ConnectionString, npg => npg.EnableRetryOnFailure(3)));
        }
        else
        {
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        }
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUser>(User);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IClinicTime, ClinicTime>();
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();
        services.AddScoped<ISearchProvider, SearchProvider>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton(Options.Create(new JwtOptions { Secret = new string('k', 48) }));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IWhatsAppProvider>(WhatsApp);
        services.AddSingleton<IAiProvider>(Ai);
        services.AddSingleton<IAiDataPolicy>(AiPolicy);
        services.AddSingleton<IFileStorage>(Files);
        services.AddSingleton<IEmailSender>(Email);
        services.AddSingleton<IPlatformInfo>(Platform);
        services.AddSingleton<IWhatsAppAccountVerifier>(WhatsAppVerifier);
        services.AddSingleton<ISecretProtector>(new AesSecretProtector(Options.Create(new SecretsOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) })));
        services.AddSingleton<Tahona.Application.Features.Import.ITabularFileReader, TabularFileReader>();
        configure?.Invoke(services);
        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();
        if (postgres) Db.Database.Migrate();
    }

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
    public AppDbContext Db => Get<AppDbContext>();

    /// <summary>A fresh unit of work (new DbContext, new tenant context), like a separate HTTP request.</summary>
    public IServiceScope NewScope() => _root.CreateScope();

    public void ActAs(Guid clinicId, Guid? userId = null)
    {
        User.ClinicId = clinicId;
        User.UserId = userId ?? Guid.NewGuid();
    }

    /// <summary>Creates a clinic with settings, an admin and one dentist, and makes it the current tenant.</summary>
    public async Task<TestClinic> AddClinicAsync(string name = "Test Clinic")
    {
        var clinic = new Clinic { Name = name, Slug = Guid.NewGuid().ToString("N") };
        ActAs(clinic.Id);
        var role = new Role { ClinicId = clinic.Id, Name = SystemRoles.Admin, IsSystem = true, Permissions = [.. Permissions.All] };
        var admin = new User { ClinicId = clinic.Id, Email = $"admin-{clinic.Slug}@test.local", FullName = "Admin", PasswordHash = "x", Role = role };
        var dentist = new Dentist { ClinicId = clinic.Id, FullName = "Dr Test" };
        Db.Clinics.Add(clinic);
        Db.ClinicSettings.Add(new ClinicSettings { ClinicId = clinic.Id });
        Db.Roles.Add(role);
        Db.Users.Add(admin);
        Db.Dentists.Add(dentist);
        Db.MessageTemplates.AddRange(
            new MessageTemplate { ClinicId = clinic.Id, Name = "Reminder", Type = MessageTemplateType.AppointmentReminder, Language = "en",
                Body = "Hi {{patient_name}}, see you {{date}} at {{time}}.", ProviderTemplateName = "appointment_reminder" },
            new MessageTemplate { ClinicId = clinic.Id, Name = "Follow-up", Type = MessageTemplateType.TreatmentFollowUp, Language = "en",
                Body = "Hi {{patient_name}}, shall we book {{treatment}}?", ProviderTemplateName = "treatment_follow_up" },
            new MessageTemplate { ClinicId = clinic.Id, Name = "Payment", Type = MessageTemplateType.PaymentReminder, Language = "en",
                Body = "Hi {{patient_name}}, your balance is {{amount}}.", ProviderTemplateName = "payment_reminder" });
        await Db.SaveChangesAsync();
        ActAs(clinic.Id, admin.Id);
        return new TestClinic(clinic.Id, admin.Id, dentist.Id);
    }

    public async Task<Patient> AddPatientAsync(string name, string phone = "0791234567", MessagingConsent consent = MessagingConsent.Granted)
    {
        var patient = new Patient
        {
            FileNumber = $"P-{Random.Shared.Next(10000, 99999)}", FullName = name, Phone = phone,
            PhoneNormalized = Tahona.Domain.Common.PhoneNumber.Normalize(phone), RegisteredAt = Clock.UtcNow.AddDays(-200),
            PreferredLanguage = "en", MessagingConsent = consent
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();
        return patient;
    }

    public void Dispose()
    {
        if (_postgres) Db.Database.EnsureDeleted();
        _scope.Dispose();
        _root.Dispose();
    }
}

public sealed record TestClinic(Guid Id, Guid AdminId, Guid DentistId);

/// <summary>A test that needs a real PostgreSQL server. Skipped, visibly, when TAHONA_TEST_POSTGRES is not set.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (TestApp.PostgresServer is null) Skip = "Set TAHONA_TEST_POSTGRES (a PostgreSQL connection string) to run this test.";
    }
}
