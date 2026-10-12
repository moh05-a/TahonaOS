using Microsoft.EntityFrameworkCore;
using Tahona.Domain.Entities;

namespace Tahona.Application.Common;

/// <summary>
/// Persistence port used by application services. All tenant-owned sets are automatically
/// filtered to the current clinic by the infrastructure implementation.
/// </summary>
public interface IAppDbContext
{
    DbSet<Clinic> Clinics { get; }
    DbSet<ClinicSettings> ClinicSettings { get; }
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Dentist> Dentists { get; }
    DbSet<Patient> Patients { get; }
    DbSet<PatientNote> PatientNotes { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<DentalTooth> DentalTeeth { get; }
    DbSet<DentalCondition> DentalConditions { get; }
    DbSet<Procedure> Procedures { get; }
    DbSet<TreatmentPlan> TreatmentPlans { get; }
    DbSet<TreatmentPlanItem> TreatmentPlanItems { get; }
    DbSet<TreatmentRecord> TreatmentRecords { get; }
    DbSet<ClinicalNote> ClinicalNotes { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<FollowUpRule> FollowUpRules { get; }
    DbSet<FollowUp> FollowUps { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PatientDocument> Documents { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The authenticated caller.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? ClinicId { get; }
    string? FullName { get; }
    string? Role { get; }
    IReadOnlyCollection<string> Permissions { get; }
    string? IpAddress { get; }
    bool HasPermission(string permission);
}

/// <summary>
/// Resolves the tenant for the current unit of work. Defaults to the caller's clinic;
/// background jobs set it explicitly via <see cref="Use"/>.
/// </summary>
public interface ITenantContext
{
    Guid? ClinicId { get; }
    void Use(Guid clinicId);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Converts between UTC (storage) and the clinic local time zone (Asia/Amman by default).</summary>
public interface IClinicTime
{
    TimeZoneInfo TimeZone { get; }
    DateTime ToLocal(DateTime utc);
    DateTime ToUtc(DateTime local);
    DateOnly Today { get; }
    DateTime StartOfDayUtc(DateOnly date);
    DateTime StartOfMonthUtc(DateOnly date);
}
