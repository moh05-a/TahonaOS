using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Persistence.Configurations;

public class FollowUpRuleConfiguration : IEntityTypeConfiguration<FollowUpRule>
{
    public void Configure(EntityTypeBuilder<FollowUpRule> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasOne(x => x.MessageTemplate).WithMany().HasForeignKey(x => x.MessageTemplateId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ClinicId, x.Trigger });
    }
}

public class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.SourceEntityType).HasMaxLength(40);
        b.Property(x => x.Outcome).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClinicId, x.Status, x.DueDate });
        b.HasIndex(x => new { x.ClinicId, x.IssueType, x.SourceEntityId });
        b.HasIndex(x => x.PatientId);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.AssignedToUser).WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Language).HasMaxLength(5);
        b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ProviderTemplateName).HasMaxLength(120);
        b.HasIndex(x => new { x.ClinicId, x.Type, x.Language });
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.Property(x => x.ToNumber).HasMaxLength(30);
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(1000);
        b.HasIndex(x => new { x.PatientId, x.CreatedAt });
        b.HasIndex(x => new { x.ClinicId, x.CreatedAt });
        b.HasIndex(x => x.ProviderMessageId);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.SentBy).WithMany().HasForeignKey(x => x.SentById).OnDelete(DeleteBehavior.SetNull);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.Link).HasMaxLength(300);
        b.Property(x => x.EntityType).HasMaxLength(40);
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PatientDocumentConfiguration : IEntityTypeConfiguration<PatientDocument>
{
    public void Configure(EntityTypeBuilder<PatientDocument> b)
    {
        b.ToTable("Documents");
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => new { x.PatientId, x.Category });
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.SetNull);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.UserName).HasMaxLength(150);
        b.Property(x => x.Action).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.ClinicId, x.Timestamp });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
