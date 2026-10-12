using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

public class AuditLog : TenantEntity
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    /// <summary>Created / Updated / Deleted, or a business action such as PaymentRecorded.</summary>
    public string Action { get; set; } = default!;
    public string EntityType { get; set; } = default!;
    public Guid? EntityId { get; set; }
    /// <summary>JSON of changed fields: { "Field": { "old": .., "new": .. } }.</summary>
    public string? Changes { get; set; }
    public string? Summary { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}
