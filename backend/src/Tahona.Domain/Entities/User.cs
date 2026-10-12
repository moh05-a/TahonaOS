using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

public class Role : TenantEntity, IAuditable
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    /// <summary>System roles (Admin, Dentist, Receptionist, Accountant) cannot be deleted.</summary>
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public class User : TenantEntity, IAuditable
{
    public string Email { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? FullNameAr { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = default!;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
}

/// <summary>Refresh tokens are stored hashed and rotated on every use. Not tenant-filtered: looked up before a tenant is known.</summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
}
