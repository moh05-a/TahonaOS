using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class MessageTemplate : TenantEntity, IAuditable
{
    public string Name { get; set; } = default!;
    public MessageTemplateType Type { get; set; }
    public MessageChannel Channel { get; set; } = MessageChannel.WhatsApp;
    public string Language { get; set; } = "en";
    /// <summary>Body with {{variables}}: patient_name, doctor, date, time, clinic_name, amount, treatment.</summary>
    public string Body { get; set; } = default!;
    /// <summary>Approved template name at the WhatsApp provider (required for business-initiated messages).</summary>
    public string? ProviderTemplateName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public class Message : TenantEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public MessageChannel Channel { get; set; }
    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public Guid? TemplateId { get; set; }
    public MessageTemplate? Template { get; set; }
    public string Body { get; set; } = default!;
    public string? ToNumber { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTime? SentAt { get; set; }
    public Guid? SentById { get; set; }
    public User? SentBy { get; set; }
    public Guid? FollowUpId { get; set; }
    public Guid? AppointmentId { get; set; }
}

public class Notification : TenantEntity
{
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    /// <summary>Frontend route to open, e.g. /patients/{id}.</summary>
    public string? Link { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime? ReadAt { get; set; }
}
