using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Persistence.Configurations;

public class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> b)
    {
        b.Property(x => x.PlanNumber).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.ClinicId, x.PlanNumber }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.HasIndex(x => x.ImportBatchId);
        b.HasIndex(x => new { x.ClinicId, x.Status });
        b.HasIndex(x => x.PatientId);
        b.HasOne(x => x.Patient).WithMany(p => p.TreatmentPlans).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TreatmentPlanItemConfiguration : IEntityTypeConfiguration<TreatmentPlanItem>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanItem> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Surfaces).HasMaxLength(10);
        b.HasIndex(x => new { x.TreatmentPlanId, x.Status });
        b.HasOne(x => x.TreatmentPlan).WithMany(p => p.Items).HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class TreatmentRecordConfiguration : IEntityTypeConfiguration<TreatmentRecord>
{
    public void Configure(EntityTypeBuilder<TreatmentRecord> b)
    {
        b.Property(x => x.ProcedureName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Surfaces).HasMaxLength(10);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.PatientId, x.PerformedAt });
        b.HasIndex(x => new { x.ClinicId, x.PerformedAt });
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ClinicalNoteConfiguration : IEntityTypeConfiguration<ClinicalNote>
{
    public void Configure(EntityTypeBuilder<ClinicalNote> b)
    {
        foreach (var p in new[] { nameof(ClinicalNote.ChiefComplaint), nameof(ClinicalNote.Diagnosis), nameof(ClinicalNote.ClinicalFindings),
                     nameof(ClinicalNote.TreatmentPerformed), nameof(ClinicalNote.Recommendations), nameof(ClinicalNote.PrescriptionNotes),
                     nameof(ClinicalNote.FollowUpInstructions) })
            b.Property(p).HasMaxLength(4000);
        b.Property(x => x.FreeText).HasMaxLength(8000);
        b.HasIndex(x => new { x.PatientId, x.NoteDate });
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.ClinicId, x.InvoiceNumber }).IsUnique();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.ClinicId, x.Status });
        b.HasIndex(x => new { x.ClinicId, x.DueDate });
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.ImportBatchId);
        b.HasOne(x => x.Patient).WithMany(p => p.Invoices).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TreatmentPlan).WithMany().HasForeignKey(x => x.TreatmentPlanId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Dentist).WithMany().HasForeignKey(x => x.DentistId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> b)
    {
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.HasOne(x => x.Invoice).WithMany(i => i.Items).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TreatmentPlanItemId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.ClinicId, x.ReceiptNumber }).IsUnique();
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClinicId, x.PaidAt });
        b.HasIndex(x => x.PatientId);
        b.HasIndex(x => x.RecoveredFollowUpId);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Invoice).WithMany(i => i.Payments).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReceivedBy).WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.SetNull);
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.HasOne(x => x.CancelledBy).WithMany().HasForeignKey(x => x.CancelledById).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.RecoveredFollowUp).WithMany().HasForeignKey(x => x.RecoveredFollowUpId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.Vendor).HasMaxLength(150);
        b.Property(x => x.Reference).HasMaxLength(100);
        b.HasIndex(x => new { x.ClinicId, x.ExpenseDate });
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}
