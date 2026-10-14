using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Persistence.Configurations;

public class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.TimeZone).HasMaxLength(60);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Country).HasMaxLength(2);
        b.Property(x => x.DefaultLanguage).HasMaxLength(5);
        b.Property(x => x.SubscriptionPlan).HasMaxLength(40);
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.HasOne(x => x.Settings).WithOne().HasForeignKey<ClinicSettings>(s => s.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClinicSettingsConfiguration : IEntityTypeConfiguration<ClinicSettings>
{
    public void Configure(EntityTypeBuilder<ClinicSettings> b)
    {
        b.HasIndex(x => x.ClinicId).IsUnique();
        b.Property(x => x.WorkingDays).HasMaxLength(20);
        b.Property(x => x.InvoicePrefix).HasMaxLength(10);
        b.OwnsOne(x => x.Recovery, r => r.ToJson());
        b.OwnsOne(x => x.Onboarding, o => o.ToJson());
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.HasIndex(x => new { x.ClinicId, x.Name }).IsUnique();
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Email).IsUnique(); // login is by email across all clinics
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.FullNameAr).HasMaxLength(150);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(x => x.PreferredLanguage).HasMaxLength(5);
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DentistConfiguration : IEntityTypeConfiguration<Dentist>
{
    public void Configure(EntityTypeBuilder<Dentist> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.FullNameAr).HasMaxLength(150);
        b.Property(x => x.Specialty).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.LicenseNumber).HasMaxLength(50);
        b.Property(x => x.Color).HasMaxLength(9);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ClinicId);
    }
}

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.Property(x => x.FileNumber).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.ClinicId, x.FileNumber }).IsUnique();
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.FullNameAr).HasMaxLength(150);
        b.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        b.Property(x => x.PhoneNormalized).HasMaxLength(20).IsRequired();
        b.Property(x => x.WhatsAppNumber).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.NationalId).HasMaxLength(40);
        b.Property(x => x.EmergencyContactName).HasMaxLength(150);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        b.Property(x => x.EmergencyContactRelation).HasMaxLength(50);
        b.Property(x => x.MedicalNotes).HasMaxLength(4000);
        b.Property(x => x.Allergies).HasMaxLength(1000);
        b.Property(x => x.MedicalConditions).HasMaxLength(1000);
        b.Property(x => x.CurrentMedications).HasMaxLength(1000);
        b.Property(x => x.InsuranceProvider).HasMaxLength(120);
        b.Property(x => x.InsurancePolicyNumber).HasMaxLength(60);
        b.Property(x => x.InsuranceCoveragePercent).HasPrecision(5, 2);
        b.Property(x => x.PreferredLanguage).HasMaxLength(5);
        b.Property(x => x.MessagingConsentSource).HasMaxLength(150);
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.SourceSystem).HasMaxLength(80);
        // Re-running an import finds the patients it already created by their source identifier.
        b.HasIndex(x => new { x.ClinicId, x.SourceSystem, x.ExternalId });
        b.HasIndex(x => x.ImportBatchId);
        b.Property(x => x.ReferralSource).HasMaxLength(80);

        // Search indexes: exact phone + name lookups, and trigram GIN indexes for fuzzy/partial matching.
        b.HasIndex(x => new { x.ClinicId, x.PhoneNormalized });
        b.HasIndex(x => new { x.ClinicId, x.FullName });
        b.HasIndex(x => x.FullName).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("IX_Patients_FullName_trgm");
        b.HasIndex(x => x.FullNameAr).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("IX_Patients_FullNameAr_trgm");
        b.HasIndex(x => x.PhoneNormalized).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("IX_Patients_Phone_trgm");
        b.HasIndex(x => new { x.ClinicId, x.Status });
        b.HasIndex(x => new { x.ClinicId, x.LastVisitAt });

        b.HasOne(x => x.PrimaryDentist).WithMany().HasForeignKey(x => x.PrimaryDentistId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PatientNoteConfiguration : IEntityTypeConfiguration<PatientNote>
{
    public void Configure(EntityTypeBuilder<PatientNote> b)
    {
        b.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.PatientId);
    }
}

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.Property(x => x.Treatment).HasMaxLength(150).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Chair).HasMaxLength(30);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.HasIndex(x => x.ImportBatchId);
        b.HasIndex(x => new { x.ClinicId, x.StartTime });
        b.HasIndex(x => new { x.DentistId, x.StartTime });
        b.HasIndex(x => new { x.PatientId, x.StartTime });
        b.HasIndex(x => new { x.ClinicId, x.Status });
        b.HasOne(x => x.Patient).WithMany(p => p.Appointments).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TreatmentPlan).WithMany().HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<FollowUp>().WithMany().HasForeignKey(x => x.FollowUpId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class DentalToothConfiguration : IEntityTypeConfiguration<DentalTooth>
{
    public void Configure(EntityTypeBuilder<DentalTooth> b)
    {
        b.HasIndex(x => new { x.PatientId, x.ToothNumber }).IsUnique();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DentalConditionConfiguration : IEntityTypeConfiguration<DentalCondition>
{
    public void Configure(EntityTypeBuilder<DentalCondition> b)
    {
        b.Property(x => x.Surfaces).HasMaxLength(10);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.PatientId, x.ToothNumber });
        b.HasOne(x => x.DentalTooth).WithMany(t => t.Conditions).HasForeignKey(x => x.DentalToothId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProcedureConfiguration : IEntityTypeConfiguration<Procedure>
{
    public void Configure(EntityTypeBuilder<Procedure> b)
    {
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(150);
        b.HasIndex(x => new { x.ClinicId, x.Code }).IsUnique();
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}
