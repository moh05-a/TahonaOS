using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

public class Dentist : TenantEntity, IAuditable
{
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string FullName { get; set; } = default!;
    public string? FullNameAr { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    /// <summary>Hex colour used on the calendar.</summary>
    public string Color { get; set; } = "#0ea5e9";
    public bool IsActive { get; set; } = true;
}
