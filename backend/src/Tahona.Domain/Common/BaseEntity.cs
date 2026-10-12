namespace Tahona.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Every clinic-owned record carries its tenant id. Global query filters enforce isolation.</summary>
public interface ITenantEntity
{
    Guid ClinicId { get; set; }
}

public abstract class TenantEntity : BaseEntity, ITenantEntity
{
    public Guid ClinicId { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Marks entities whose changes are written to the audit log automatically.</summary>
public interface IAuditable;
