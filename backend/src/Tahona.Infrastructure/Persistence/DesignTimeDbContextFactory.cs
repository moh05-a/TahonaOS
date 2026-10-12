using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tahona.Application.Common;
using Tahona.Infrastructure.Services;

namespace Tahona.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to build the model for migrations (no tenant, no user).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                         ?? "Host=localhost;Port=5432;Database=tahona;Username=tahona;Password=design-time-only";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        var user = new NoUser();
        return new AppDbContext(options, new TenantContext(user), user, new SystemClock());
    }

    private sealed class NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? ClinicId => null;
        public string? FullName => null;
        public string? Role => null;
        public IReadOnlyCollection<string> Permissions => [];
        public string? IpAddress => null;
        public bool HasPermission(string permission) => false;
    }
}
