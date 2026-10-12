using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Staff;

public record StaffUserDto(
    Guid Id, string Email, string FullName, string? FullNameAr, string? Phone, Guid RoleId, string RoleName,
    bool IsActive, DateTime? LastLoginAt, DateTime CreatedAt, Guid? DentistId);

public record CreateStaffRequest(
    string Email, string FullName, string? FullNameAr, string? Phone, Guid RoleId, string Password,
    bool CreateDentistProfile, string? Specialty, string? DentistColor);

public record UpdateStaffRequest(string FullName, string? FullNameAr, string? Phone, Guid RoleId, bool IsActive);

public record ResetPasswordRequest(string NewPassword);

public record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);

public record UpdateRoleRequest(string? Description, IReadOnlyList<string> Permissions);

public class CreateStaffValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]")
            .WithMessage("Password must be at least 8 characters with upper, lower case letters and a digit.");
        RuleFor(x => x.DentistColor).Matches("^#[0-9a-fA-F]{6}$").When(x => !string.IsNullOrEmpty(x.DentistColor));
    }
}

public class UpdateStaffValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator() =>
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]")
            .WithMessage("Password must be at least 8 characters with upper, lower case letters and a digit.");
}

public class StaffService(IAppDbContext db, IPasswordHasher<User> hasher, ICurrentUser currentUser, IClock clock)
{
    public async Task<IReadOnlyList<StaffUserDto>> ListAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.FullName)
            .Select(u => new StaffUserDto(u.Id, u.Email, u.FullName, u.FullNameAr, u.Phone, u.RoleId, u.Role.Name,
                u.IsActive, u.LastLoginAt, u.CreatedAt,
                db.Dentists.Where(d => d.UserId == u.Id).Select(d => (Guid?)d.Id).FirstOrDefault()))
            .ToListAsync(ct);

    public async Task<Guid> CreateAsync(CreateStaffRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw RequestValidationException.For("email", "This email is already in use.");
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
                   ?? throw RequestValidationException.For("roleId", "Role not found.");

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            FullNameAr = request.FullNameAr?.Trim(),
            Phone = request.Phone?.Trim(),
            RoleId = role.Id
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);

        if (request.CreateDentistProfile)
        {
            db.Dentists.Add(new Dentist
            {
                UserId = user.Id,
                FullName = user.FullName,
                FullNameAr = user.FullNameAr,
                Phone = user.Phone,
                Email = user.Email,
                Specialty = request.Specialty,
                Color = request.DentistColor ?? "#0ea5e9"
            });
        }

        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateStaffRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct)
                   ?? throw RequestValidationException.For("roleId", "Role not found.");

        if (id == currentUser.UserId && (!request.IsActive || role.Name != user.Role.Name && user.Role.Name == SystemRoles.Admin))
            throw new BusinessRuleException("You cannot deactivate yourself or remove your own admin role.");

        if (user.Role.Name == SystemRoles.Admin && (role.Name != SystemRoles.Admin || !request.IsActive))
        {
            var otherAdmins = await db.Users.CountAsync(u => u.Id != id && u.IsActive && u.Role.Name == SystemRoles.Admin, ct);
            if (otherAdmins == 0) throw new BusinessRuleException("The clinic must keep at least one active administrator.");
        }

        user.FullName = request.FullName.Trim();
        user.FullNameAr = request.FullNameAr?.Trim();
        user.Phone = request.Phone?.Trim();
        user.RoleId = role.Id;
        user.IsActive = request.IsActive;

        if (!request.IsActive)
        {
            var now = clock.UtcNow;
            var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
            tokens.ForEach(t => t.RevokedAt = now);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        var now = clock.UtcNow;
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.RevokedAt = now);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions, db.Users.Count(u => u.RoleId == r.Id)))
            .ToListAsync(ct);

    public async Task UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);
        if (role.Name == SystemRoles.Admin)
            throw new BusinessRuleException("The Admin role always has every permission and cannot be edited.");

        var unknown = request.Permissions.Except(Permissions.All).ToList();
        if (unknown.Count > 0) throw RequestValidationException.For("permissions", $"Unknown permissions: {string.Join(", ", unknown)}");

        role.Description = request.Description?.Trim();
        role.Permissions = request.Permissions.Distinct().ToList();
        await db.SaveChangesAsync(ct);
    }
}
