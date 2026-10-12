using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Auth;

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ClinicInfoDto(Guid Id, string Name, string? NameAr, string Currency, string TimeZone, string DefaultLanguage);

public record UserProfileDto(
    Guid Id, string Email, string FullName, string? FullNameAr, string Role,
    IReadOnlyList<string> Permissions, string PreferredLanguage, Guid? DentistId, ClinicInfoDto Clinic);

public record AuthResult(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, UserProfileDto User);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public class AuthService(
    IAppDbContext db,
    ITokenService tokens,
    IPasswordHasher<User> hasher,
    ICurrentUser currentUser,
    IClock clock)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        // Same message for unknown user / wrong password to avoid account enumeration.
        const string invalid = "Invalid email or password.";
        if (user is null || !user.IsActive) throw new UnauthorizedException(invalid);

        var now = clock.UtcNow;
        if (user.LockedUntil > now)
            throw new UnauthorizedException("Account temporarily locked after repeated failed attempts. Try again later.");

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntil = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException(invalid);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);

        var clinic = await db.Clinics.IgnoreQueryFilters().FirstAsync(c => c.Id == user.ClinicId, ct);
        if (!clinic.IsActive) throw new UnauthorizedException("This clinic account is not active.");

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        return await IssueTokensAsync(user, clinic, ip, null, ct);
    }

    public async Task<AuthResult> RefreshAsync(string rawRefreshToken, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) throw new UnauthorizedException("Session expired.");

        var hash = tokens.HashToken(rawRefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.Role)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        var now = clock.UtcNow;
        if (stored is null) throw new UnauthorizedException("Session expired.");

        if (stored.RevokedAt is not null)
        {
            // Reuse of a rotated token: likely theft. Revoke the whole chain for this user.
            var active = await db.RefreshTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null).ToListAsync(ct);
            active.ForEach(t => t.RevokedAt = now);
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("Session expired.");
        }

        if (stored.ExpiresAt <= now || !stored.User.IsActive) throw new UnauthorizedException("Session expired.");

        var clinic = await db.Clinics.IgnoreQueryFilters().FirstAsync(c => c.Id == stored.User.ClinicId, ct);
        if (!clinic.IsActive) throw new UnauthorizedException("This clinic account is not active.");
        return await IssueTokensAsync(stored.User, clinic, ip, stored, ct);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var hash = tokens.HashToken(rawRefreshToken);
        var stored = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new UnauthorizedException();
        var clinic = await db.Clinics.FirstAsync(c => c.Id == user.ClinicId, ct);
        var dentistId = await db.Dentists.Where(d => d.UserId == user.Id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        return ToProfile(user, clinic, dentistId);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw RequestValidationException.For(nameof(request.CurrentPassword), "Current password is incorrect.");

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        // Sign out other sessions.
        var now = clock.UtcNow;
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.RevokedAt = now);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetLanguageAsync(string language, CancellationToken ct)
    {
        if (language is not ("en" or "ar")) throw RequestValidationException.For("language", "Supported languages: en, ar.");
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.PreferredLanguage = language;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, Clinic clinic, string? ip, RefreshToken? replacing, CancellationToken ct)
    {
        var dentistId = await db.Dentists.IgnoreQueryFilters()
            .Where(d => d.UserId == user.Id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);

        var access = tokens.CreateAccessToken(user, user.Role, clinic, dentistId);
        var refresh = tokens.CreateRefreshToken();
        var now = clock.UtcNow;

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenHash = refresh.TokenHash;
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt,
            CreatedByIp = ip
        });

        // Housekeeping: drop this user's long-expired tokens.
        var cutoff = now.AddDays(-30);
        var stale = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && t.ExpiresAt < cutoff).ToListAsync(ct);
        db.RefreshTokens.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
        return new AuthResult(access.Token, access.ExpiresAt, refresh.RawToken, refresh.ExpiresAt, ToProfile(user, clinic, dentistId));
    }

    private static UserProfileDto ToProfile(User user, Clinic clinic, Guid? dentistId) => new(
        user.Id, user.Email, user.FullName, user.FullNameAr, user.Role.Name,
        user.Role.Permissions, user.PreferredLanguage, dentistId,
        new ClinicInfoDto(clinic.Id, clinic.Name, clinic.NameAr, clinic.Currency, clinic.TimeZone, clinic.DefaultLanguage));
}
