using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Messaging;

/// <param name="Mode">"clinic" = the clinic's own number; "shared" = credentials set for the whole deployment; "manual" = staff send from WhatsApp.</param>
/// <param name="CanConnect">False when the deployment has no encryption key, so a token cannot be stored.</param>
public record WhatsAppConnectionDto(string Mode, bool CanConnect, string? PhoneNumberId, string? DisplayPhoneNumber, string? VerifiedName, DateTime? ConnectedAt);

public record ConnectWhatsAppRequest(string PhoneNumberId, string AccessToken);

public class ConnectWhatsAppValidator : AbstractValidator<ConnectWhatsAppRequest>
{
    public ConnectWhatsAppValidator()
    {
        RuleFor(x => x.PhoneNumberId).NotEmpty().MaximumLength(40).Matches("^[0-9]+$").WithMessage("The phone number ID is the numeric ID shown in Meta's WhatsApp settings.");
        RuleFor(x => x.AccessToken).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>
/// Connects a clinic's own WhatsApp Business number. Credentials are checked with Meta first, the token is stored
/// encrypted, and it is never sent back to the browser. Each clinic sends from its own number.
/// </summary>
public class WhatsAppSettingsService(IAppDbContext db, IWhatsAppAccountVerifier verifier, ISecretProtector secrets, IWhatsAppProvider provider,
    ICurrentUser currentUser, IClock clock)
{
    public async Task<WhatsAppConnectionDto> GetAsync(CancellationToken ct)
    {
        var own = await db.ClinicWhatsApps.AsNoTracking().FirstOrDefaultAsync(ct);
        if (own is { IsEnabled: true })
            return new WhatsAppConnectionDto("clinic", secrets.IsAvailable, own.PhoneNumberId, own.DisplayPhoneNumber, own.VerifiedName, own.ConnectedAt);
        return new WhatsAppConnectionDto(provider.IsConfigured ? "shared" : "manual", secrets.IsAvailable, null, null, null, null);
    }

    public async Task<WhatsAppConnectionDto> ConnectAsync(ConnectWhatsAppRequest request, CancellationToken ct)
    {
        if (!secrets.IsAvailable)
            throw new BusinessRuleException("WhatsApp credentials cannot be stored on this server yet. Ask support to configure the encryption key.");

        var phoneNumberId = request.PhoneNumberId.Trim();
        var token = request.AccessToken.Trim();
        // A number belongs to one clinic. The filter is bypassed only to check that nobody else holds it.
        var clinicId = await db.Clinics.Select(c => c.Id).FirstAsync(ct);
        if (await db.ClinicWhatsApps.IgnoreQueryFilters().AnyAsync(w => w.PhoneNumberId == phoneNumberId && w.ClinicId != clinicId, ct))
            throw new ConflictException("This WhatsApp number is already connected to another clinic.");

        var (number, error) = await verifier.VerifyAsync(phoneNumberId, token, ct);
        if (number is null) throw new BusinessRuleException($"WhatsApp did not accept these details: {error ?? "unknown error"}");

        var existing = await db.ClinicWhatsApps.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            existing = new ClinicWhatsApp();
            db.ClinicWhatsApps.Add(existing);
        }
        existing.PhoneNumberId = phoneNumberId;
        existing.DisplayPhoneNumber = number.DisplayPhoneNumber;
        existing.VerifiedName = number.VerifiedName;
        existing.EncryptedAccessToken = secrets.Protect(token);
        existing.IsEnabled = true;
        existing.ConnectedAt = clock.UtcNow;
        existing.ConnectedById = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>Removes the clinic's number and its stored token. Messaging falls back to manual sending.</summary>
    public async Task<WhatsAppConnectionDto> DisconnectAsync(CancellationToken ct)
    {
        var existing = await db.ClinicWhatsApps.FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            db.ClinicWhatsApps.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(ct);
    }
}
