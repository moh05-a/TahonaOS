using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Persistence;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IClock clock) : DbContext(options), IAppDbContext
{
    private static readonly HashSet<string> AuditIgnoredProperties =
        [nameof(BaseEntity.CreatedAt), nameof(BaseEntity.UpdatedAt), nameof(User.PasswordHash), nameof(ClinicWhatsApp.EncryptedAccessToken), nameof(User.FailedLoginCount),
         nameof(User.LastLoginAt), nameof(User.LockedUntil), nameof(Domain.Entities.ClinicSettings.NextInvoiceNumber),
         nameof(Domain.Entities.ClinicSettings.NextPatientNumber), nameof(Domain.Entities.ClinicSettings.NextPlanNumber), nameof(Domain.Entities.ClinicSettings.NextReceiptNumber)];

    /// <summary>Used by query filters. EF parameterises this per query, so it is evaluated for every execution.</summary>
    private Guid CurrentClinicId => tenant.ClinicId ?? Guid.Empty;

    /// <summary>Disable automatic audit entries (bulk seeding, background bookkeeping).</summary>
    public bool SuppressAudit { get; set; }

    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<ClinicSettings> ClinicSettings => Set<ClinicSettings>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Dentist> Dentists => Set<Dentist>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientNote> PatientNotes => Set<PatientNote>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<DentalTooth> DentalTeeth => Set<DentalTooth>();
    public DbSet<DentalCondition> DentalConditions => Set<DentalCondition>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<TreatmentPlan> TreatmentPlans => Set<TreatmentPlan>();
    public DbSet<TreatmentPlanItem> TreatmentPlanItems => Set<TreatmentPlanItem>();
    public DbSet<TreatmentRecord> TreatmentRecords => Set<TreatmentRecord>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<FollowUpRule> FollowUpRules => Set<FollowUpRule>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ClinicWhatsApp> ClinicWhatsApps => Set<ClinicWhatsApp>();
    public DbSet<PatientDocument> Documents => Set<PatientDocument>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    public DbSet<ClinicInvitation> ClinicInvitations => Set<ClinicInvitation>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as readable strings; money uses 3 decimals (JOD fils).
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        builder.Properties<decimal>().HavePrecision(14, 3);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
            modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tenant isolation (+ soft delete) for every clinic-owned entity.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (!typeof(ITenantEntity).IsAssignableFrom(clr) || entityType.IsOwned()) continue;

            var method = typeof(AppDbContext)
                .GetMethod(nameof(BuildTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(clr);
            modelBuilder.Entity(clr).HasQueryFilter((LambdaExpression)method.Invoke(this, null)!);
        }

        modelBuilder.Entity<Clinic>().HasQueryFilter(c => c.Id == CurrentClinicId);
    }

    private LambdaExpression BuildTenantFilter<T>() where T : class, ITenantEntity
    {
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(T)))
        {
            Expression<Func<T, bool>> softFilter =
                e => e.ClinicId == CurrentClinicId && !((ISoftDelete)e).IsDeleted;
            return softFilter;
        }

        Expression<Func<T, bool>> filter = e => e.ClinicId == CurrentClinicId;
        return filter;
    }

    public async Task<T> WithLocksAsync<T>(IReadOnlyCollection<string> lockKeys, Func<Task<T>> action, CancellationToken ct)
    {
        // Advisory locks are a PostgreSQL feature; other providers (tests) are single-threaded per context.
        if (!Database.IsNpgsql()) return await action();

        // Sorted, so two requests needing the same keys always take them in the same order (no deadlock).
        var keys = lockKeys.Distinct().Order(StringComparer.Ordinal).ToList();
        if (Database.CurrentTransaction is not null)
        {
            await AcquireAsync(keys, ct);
            return await action();
        }

        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            ChangeTracker.Clear(); // a retried attempt starts from a clean slate
            await using var tx = await Database.BeginTransactionAsync(ct);
            await AcquireAsync(keys, ct);
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    /// <summary>Transaction-scoped advisory locks: released automatically on commit or rollback.</summary>
    private async Task AcquireAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys)
            await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", [key], ct);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var audits = new List<AuditLog>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is BaseEntity baseEntity)
            {
                if (entry.State == EntityState.Added && baseEntity.CreatedAt == default) baseEntity.CreatedAt = now;
                if (entry.State == EntityState.Modified) baseEntity.UpdatedAt = now;
            }

            if (entry is { State: EntityState.Added, Entity: ITenantEntity tenantEntity } && tenantEntity.ClinicId == Guid.Empty)
            {
                tenantEntity.ClinicId = tenant.ClinicId
                    ?? throw new InvalidOperationException("Cannot save a tenant entity without a clinic context.");
            }

            if (entry is { State: EntityState.Deleted, Entity: ISoftDelete softDelete })
            {
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = now;
            }

            if (!SuppressAudit && entry.Entity is IAuditable and ITenantEntity
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var audit = BuildAudit(entry, now);
                if (audit is not null) audits.Add(audit);
            }
        }

        if (audits.Count > 0) AuditLogs.AddRange(audits);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private AuditLog? BuildAudit(EntityEntry entry, DateTime now)
    {
        var tenantEntity = (ITenantEntity)entry.Entity;
        var action = entry.State switch
        {
            EntityState.Added => "Created",
            EntityState.Deleted => "Deleted",
            _ when entry.Entity is ISoftDelete { IsDeleted: true } && entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified => "Deleted",
            _ => "Updated"
        };

        var changes = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (AuditIgnoredProperties.Contains(name) || name == nameof(ITenantEntity.ClinicId)) continue;

            if (entry.State == EntityState.Added)
            {
                if (prop.CurrentValue is not null && name != nameof(BaseEntity.Id)) changes[name] = prop.CurrentValue;
            }
            else if (entry.State == EntityState.Modified && prop.IsModified && !Equals(prop.OriginalValue, prop.CurrentValue))
            {
                changes[name] = new { old = prop.OriginalValue, @new = prop.CurrentValue };
            }
        }

        if (entry.State == EntityState.Modified && changes.Count == 0) return null;

        return new AuditLog
        {
            ClinicId = tenantEntity.ClinicId == Guid.Empty ? tenant.ClinicId ?? Guid.Empty : tenantEntity.ClinicId,
            UserId = currentUser.UserId,
            UserName = currentUser.FullName ?? "System",
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entry.Entity is BaseEntity b ? b.Id : null,
            Changes = changes.Count > 0 ? JsonSerializer.Serialize(changes, AuditJson) : null,
            Timestamp = now,
            IpAddress = currentUser.IpAddress
        };
    }

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
