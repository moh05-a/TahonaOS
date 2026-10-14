using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Auth;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Provisioning;

public record CreateInvitationCommand(string Email, string? ClinicName, string? ContactName, string Plan, int TrialDays, string? Note,
    string CreatedBy, int ValidDays = 14);

/// <param name="Token">The raw token. Shown once, when the invitation is created; it cannot be recovered afterwards.</param>
/// <param name="EmailSent">True when the link was also e-mailed to the invited address.</param>
public record IssuedInvitation(Guid Id, string Email, string Token, DateTime ExpiresAt, bool EmailSent);

public record InvitationListItem(Guid Id, string Email, string? ClinicName, string Plan, int TrialDays, DateTime CreatedAt, DateTime ExpiresAt,
    DateTime? AcceptedAt, DateTime? RevokedAt, Guid? ClinicId, string CreatedBy, string State);

/// <summary>What the invited person sees before creating the account.</summary>
public record InvitationInfoDto(string Email, string? ClinicName, string? ContactName, string Plan, int TrialDays, DateTime ExpiresAt);

public record AcceptInvitationRequest(string Token, string ClinicName, string? ClinicNameAr, string FullName, string Password,
    string Language, string? Phone, bool IsDentist);

public class AcceptInvitationValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ClinicName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ClinicNameAr).MaximumLength(200);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Language).Must(l => l is "en" or "ar").WithMessage("Language must be en or ar.");
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

/// <summary>
/// Invitation-based sign-up. An operator issues an invitation for one e-mail address; the clinic owner opens the link,
/// names the clinic and chooses a password. There is no open self-registration, so nobody can create accounts in bulk
/// or claim an address they were not invited with.
/// </summary>
public class InvitationService(IAppDbContext db, ClinicProvisioningService provisioning, AuthService auth, IEmailSender mailer, IPlatformInfo platform,
    IClock clock)
{
    private const string Invalid = "This invitation link is not valid or has expired. Ask for a new one.";

    // ------------------------------------------------------------------ operator side

    public async Task<IssuedInvitation> CreateAsync(CreateInvitationCommand cmd, CancellationToken ct)
    {
        var email = cmd.Email.Trim().ToLowerInvariant();
        if (email.Length is 0 or > 200 || !email.Contains('@')) throw RequestValidationException.For("email", "Enter a valid e-mail address.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this e-mail address already exists.");

        var now = clock.UtcNow;
        // One open invitation per address: issuing a new one cancels the previous link.
        var open = await db.ClinicInvitations.Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null).ToListAsync(ct);
        open.ForEach(i => i.RevokedAt = now);

        var (raw, hash) = SecureToken.Generate();
        var invitation = new ClinicInvitation
        {
            Email = email,
            ClinicName = Clean(cmd.ClinicName),
            ContactName = Clean(cmd.ContactName),
            TokenHash = hash,
            ExpiresAt = now.AddDays(Math.Clamp(cmd.ValidDays, 1, 60)),
            Plan = SubscriptionPlans.All.Contains(cmd.Plan) ? cmd.Plan : SubscriptionPlans.Founding,
            TrialDays = Math.Clamp(cmd.TrialDays, 1, 365),
            Note = Clean(cmd.Note),
            CreatedBy = cmd.CreatedBy
        };
        db.ClinicInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        var sent = mailer.IsConfigured && await mailer.SendAsync(
            AccountEmails.Invitation(email, $"{platform.PublicUrl}/welcome?token={raw}", invitation.ClinicName, invitation.TrialDays, invitation.ExpiresAt), ct);
        return new IssuedInvitation(invitation.Id, email, raw, invitation.ExpiresAt, sent);
    }

    public async Task<IReadOnlyList<InvitationListItem>> ListAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await db.ClinicInvitations.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(i => new InvitationListItem(i.Id, i.Email, i.ClinicName, i.Plan, i.TrialDays, i.CreatedAt, i.ExpiresAt, i.AcceptedAt,
            i.RevokedAt, i.ClinicId, i.CreatedBy,
            i.AcceptedAt is not null ? "Accepted" : i.RevokedAt is not null ? "Revoked" : i.ExpiresAt <= now ? "Expired" : "Open")).ToList();
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var invitation = await db.ClinicInvitations.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invitation", id);
        if (invitation.AcceptedAt is not null) throw new BusinessRuleException("This invitation was already accepted.");
        invitation.RevokedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ invited person

    public async Task<InvitationInfoDto> GetAsync(string token, CancellationToken ct)
    {
        var i = await FindOpenAsync(token, ct) ?? throw new BusinessRuleException(Invalid);
        return new InvitationInfoDto(i.Email, i.ClinicName, i.ContactName, i.Plan, i.TrialDays, i.ExpiresAt);
    }

    /// <summary>Creates the clinic and its administrator, then signs them in. A token works exactly once.</summary>
    public async Task<AuthResult> AcceptAsync(AcceptInvitationRequest request, string? ip, CancellationToken ct)
    {
        var hash = SecureToken.Hash(request.Token);
        var email = await db.WithLocksAsync([$"invitation:{hash}"], async () =>
        {
            var invitation = await FindOpenAsync(request.Token, ct) ?? throw new BusinessRuleException(Invalid);
            var clinic = await provisioning.ProvisionAsync(new ProvisionClinicCommand(request.ClinicName, request.ClinicNameAr, invitation.Email,
                request.FullName, request.Password, request.Language, request.Phone, invitation.Plan, invitation.TrialDays, request.IsDentist), ct);

            invitation.AcceptedAt = clock.UtcNow;
            invitation.ClinicId = clinic.ClinicId;
            await db.SaveChangesAsync(ct);
            return invitation.Email;
        }, ct);

        return await auth.LoginAsync(new LoginRequest(email, request.Password), ip, ct);
    }

    private async Task<ClinicInvitation?> FindOpenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = SecureToken.Hash(token);
        var now = clock.UtcNow;
        return await db.ClinicInvitations.FirstOrDefaultAsync(
            i => i.TokenHash == hash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
