using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Persistence.Configurations;

public class ClinicInvitationConfiguration : IEntityTypeConfiguration<ClinicInvitation>
{
    public void Configure(EntityTypeBuilder<ClinicInvitation> b)
    {
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.ClinicName).HasMaxLength(200);
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.Plan).HasMaxLength(40);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.Email);
    }
}

public class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.CreatedBy).HasMaxLength(150).IsRequired();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SourceSystem).HasMaxLength(80).IsRequired();
        b.Property(x => x.HeadersJson).HasColumnType("jsonb");
        b.Property(x => x.MappingJson).HasColumnType("jsonb");
        b.Property(x => x.OptionsJson).HasColumnType("jsonb");
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClinicId, x.CreatedAt });
        b.HasIndex(x => new { x.ClinicId, x.FileHash });
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> b)
    {
        b.Property(x => x.DataJson).HasColumnType("jsonb");
        b.Property(x => x.MessagesJson).HasColumnType("jsonb");
        b.Property(x => x.EntityType).HasMaxLength(60);
        b.HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique();
        b.HasOne(x => x.Batch).WithMany(x => x.Rows).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
