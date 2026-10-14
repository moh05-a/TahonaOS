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
    /// <summary>Sender number for inbound messages.</summary>
    public string? FromNumber { get; set; }
    /// <summary>True when the automation engine sent it, false when a staff member did.</summary>
    public bool IsAutomated { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public Guid? SentById { get; set; }
    public User? SentBy { get; set; }
    public Guid? FollowUpId { get; set; }
    public Guid? AppointmentId { get; set; }
}

/// <summary>
/// A clinic's own WhatsApp Business number on the Meta Cloud API. The access token is stored encrypted and is
/// never returned by the API or written to the audit log.
/// </summary>
public class ClinicWhatsApp : TenantEntity, IAuditable
{
    /// <summary>Meta's id for the phone number. Unique across clinics: it routes incoming webhook events.</summary>
    public string PhoneNumberId { get; set; } = default!;
    public string? DisplayPhoneNumber { get; set; }
    public string? VerifiedName { get; set; }
    public string EncryptedAccessToken { get; set; } = default!;
    public bool IsEnabled { get; set; } = true;
    public DateTime ConnectedAt { get; set; }
    public Guid? ConnectedById { get; set; }
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
