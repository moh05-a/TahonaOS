using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Tahona.Api.Infrastructure;
using Tahona.Api.Operator;
using Tahona.Application;
using Tahona.Application.Common;
using Tahona.Infrastructure;
using Tahona.Infrastructure.Auth;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Persistence.Seed;

// Load secrets from the repository-root .env for local development (never committed; see .env.example).
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Outside development, logs are one JSON object per line so a log collector can index and alert on them.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o =>
    {
        o.UseUtcTimestamp = true;
        o.IncludeScopes = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

// ------------------------------------------------------------------ configuration guard rails
var jwtSecret = config["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be set (≥ 32 characters) via the JWT__SECRET environment variable. See .env.example.");

// ------------------------------------------------------------------ services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);

builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"] ?? "tahona",
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"] ?? "tahona-app",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Credential attempts: strict per IP (account lockout additionally protects each user).
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
    // Session refresh happens on every page load; a whole clinic often shares one public IP.
    o.AddPolicy("refresh", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("ai", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst("sub")?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = (config["Cors:Origins"] ?? "http://localhost:5173").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Tahona Dental API",
        Version = "v1",
        Description = "Dental clinic management with a Revenue Recovery Engine. Authenticate via POST /api/auth/login and use the access token as a Bearer token."
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by /api/auth/login"
    });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    o.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
});

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Behind a reverse proxy the client address arrives in X-Forwarded-For. Only proxies listed in Proxy:TrustedNetworks
// (comma-separated CIDR ranges) are believed; loopback is trusted by default.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var cidr in (config["Proxy:TrustedNetworks"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

var app = builder.Build();

// ------------------------------------------------------------------ operator commands (no web server)
if (OperatorCli.IsOperatorCommand(args))
{
    await MigrateAsync(app, allowReset: false);
    Environment.ExitCode = await OperatorCli.RunAsync(app, args);
    return;
}

// ------------------------------------------------------------------ database & demo data
await InitializeDatabaseAsync(app);

// ------------------------------------------------------------------ pipeline
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (app.Environment.IsDevelopment() || config.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Tahona API");
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<RequestLogMiddleware>();
app.UseMiddleware<ClinicAccessMiddleware>();

app.MapControllers();
// Liveness: the process is up. Readiness: the database answers.
app.MapHealthChecks("/health", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous().ExcludeFromDescription();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, title: "Endpoint not found.")).AllowAnonymous();

app.Run();

// ------------------------------------------------------------------ helpers

static async Task MigrateAsync(WebApplication app, bool allowReset)
{
    var config = app.Configuration;
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (allowReset && config.GetValue("Seed:ResetDemo", false))
    {
        if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Seed:ResetDemo is only allowed in Development.");
        logger.LogWarning("Seed:ResetDemo=true — dropping and recreating the database.");
        await db.Database.EnsureDeletedAsync();
    }

    if (config.GetValue("Database:AutoMigrate", true))
    {
        logger.LogInformation("Applying database migrations…");
        await db.Database.MigrateAsync();
    }
}

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await MigrateAsync(app, allowReset: true);

    var config = app.Configuration;
    if (config.GetValue("Seed:DemoData", false))
    {
        // Demo accounts use well-known e-mail addresses, so they never appear outside Development by accident.
        if (!app.Environment.IsDevelopment() && !config.GetValue("Seed:AllowOutsideDevelopment", false))
            throw new InvalidOperationException(
                "Seed:DemoData is enabled outside Development. Remove SEED__DEMODATA, or set SEED__ALLOWOUTSIDEDEVELOPMENT=true for a deliberate demo site.");
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Seed:DemoData is enabled but SEED__DEMOPASSWORD is not set.");
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(password, CancellationToken.None);
    }
}

static void LoadDotEnv()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var file = Path.Combine(dir.FullName, ".env");
        if (File.Exists(file))
        {
            DotNetEnv.Env.Load(file, new DotNetEnv.LoadOptions(setEnvVars: true, clobberExistingVars: false));
            return;
        }
        dir = dir.Parent;
    }
}

public partial class Program;
