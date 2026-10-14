using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Auth;

public record ResetPasswordWithTokenRequest(string Token, string NewPassword);

/// <param name="Token">The raw token. Shown once to whoever issued it.</param>
public record IssuedPasswordReset(string Email, string FullName, string Token, DateTime ExpiresAt);

public class ResetPasswordWithTokenValidator : AbstractValidator<ResetPasswordWithTokenRequest>
{
    public ResetPasswordWithTokenValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

/// <summary>
/// Password recovery by single-use link. Until e-mail delivery is configured, an operator issues the link after
/// confirming the person's identity and passes it on; inside a clinic, the administrator resets staff passwords directly.
/// </summary>
public class AccountRecoveryService(IAppDbContext db, IPasswordHasher<User> hasher, IEmailSender email, IPlatformInfo platform, IClock clock)
{
    private static readonly TimeSpan Validity = TimeSpan.FromHours(24);

    /// <summary>A second request for the same account within this time is ignored, so nobody can flood an inbox.</summary>
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(2);

    /// <summary>Whether people can reset their own password by e-mail on this deployment.</summary>
    public bool SelfServiceAvailable => email.IsConfigured;

    /// <summary>
    /// Self-service: e-mails a reset link to the address if it belongs to an active account. It never says whether
    /// it did, so the endpoint cannot be used to find out which addresses have accounts.
    /// </summary>
    public async Task RequestByEmailAsync(string address, CancellationToken ct)
    {
        if (!email.IsConfigured || string.IsNullOrWhiteSpace(address)) return;
        var normalised = address.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalised && u.IsActive, ct);
        if (user is null) return;

        var recently = clock.UtcNow - RequestCooldown;
        if (await db.UserTokens.AnyAsync(t => t.UserId == user.Id && t.Purpose == UserTokenPurpose.PasswordReset && t.CreatedAt >= recently, ct)) return;

        var issued = await IssueAsync(normalised, "Self-service", ct);
        await email.SendAsync(AccountEmails.PasswordReset(issued.Email, $"{platform.PublicUrl}/reset-password?token={issued.Token}", issued.ExpiresAt), ct);
    }

    /// <summary>Issues a reset link token for the account with this e-mail address. Earlier unused links stop working.</summary>
    public async Task<IssuedPasswordReset> IssueAsync(string email, string issuedBy, CancellationToken ct)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == normalised, ct)
                   ?? throw new NotFoundException("User", normalised);
        if (!user.IsActive) throw new BusinessRuleException("This user account is deactivated.");

        var now = clock.UtcNow;
        var earlier = await db.UserTokens.Where(t => t.UserId == user.Id && t.Purpose == UserTokenPurpose.PasswordReset && t.UsedAt == null).ToListAsync(ct);
        earlier.ForEach(t => t.UsedAt = now);

        var (raw, hash) = SecureToken.Generate();
        var token = new UserToken { UserId = user.Id, Purpose = UserTokenPurpose.PasswordReset, TokenHash = hash, ExpiresAt = now.Add(Validity), CreatedBy = issuedBy };
        db.UserTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return new IssuedPasswordReset(user.Email, user.FullName, raw, token.ExpiresAt);
    }

    /// <summary>Sets a new password from a reset link. The link works once; every existing session of the user is signed out.</summary>
    public async Task ResetAsync(ResetPasswordWithTokenRequest request, CancellationToken ct)
    {
        const string invalid = "This reset link is not valid or has expired. Ask for a new one.";
        var hash = SecureToken.Hash(request.Token);
        await db.WithLocksAsync([$"user-token:{hash}"], async () =>
        {
            var now = clock.UtcNow;
            var token = await db.UserTokens.FirstOrDefaultAsync(
                            t => t.TokenHash == hash && t.Purpose == UserTokenPurpose.PasswordReset && t.UsedAt == null && t.ExpiresAt > now, ct)
                        ?? throw new BusinessRuleException(invalid);
            var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId && u.IsActive, ct)
                       ?? throw new BusinessRuleException(invalid);

            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            token.UsedAt = now;

            var sessions = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
            sessions.ForEach(t => t.RevokedAt = now);
            return await db.SaveChangesAsync(ct);
        }, ct);
    }
}
