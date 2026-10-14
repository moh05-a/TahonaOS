using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Provisioning;

public record ProvisionClinicCommand(
    string ClinicName, string? ClinicNameAr, string AdminEmail, string AdminFullName, string AdminPassword,
    string Language, string? Phone, string Plan, int TrialDays, bool AdminIsDentist);

public record ProvisionedClinic(Guid ClinicId, Guid AdminUserId, string Slug);

/// <summary>Random single-use tokens for invitation and password-reset links. Only the SHA-256 hash is ever stored.</summary>
public static class SecureToken
{
    public static (string Raw, string Hash) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (raw, Hash(raw));
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.Trim())));
}

/// <summary>
/// Creates a working, empty clinic: the tenant, its settings, the system roles, the first administrator and the
/// starter catalogue. Nothing else is needed before the clinic can sign in — no manual database work.
/// </summary>
public class ClinicProvisioningService(IAppDbContext db, ITenantContext tenant, IPasswordHasher<User> hasher, IClock clock)
{
    public async Task<ProvisionedClinic> ProvisionAsync(ProvisionClinicCommand cmd, CancellationToken ct)
    {
        var email = cmd.AdminEmail.Trim().ToLowerInvariant();
        // E-mail addresses are unique across all clinics: the address alone identifies the account at sign-in.
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this e-mail address already exists. Sign in, or ask for a password reset.");

        var now = clock.UtcNow;
        var clinic = new Clinic
        {
            Name = cmd.ClinicName.Trim(),
            NameAr = string.IsNullOrWhiteSpace(cmd.ClinicNameAr) ? null : cmd.ClinicNameAr.Trim(),
            Slug = Slugify(cmd.ClinicName),
            Phone = string.IsNullOrWhiteSpace(cmd.Phone) ? null : cmd.Phone.Trim(),
            Email = email,
            DefaultLanguage = cmd.Language == "en" ? "en" : "ar",
            SubscriptionPlan = SubscriptionPlans.All.Contains(cmd.Plan) ? cmd.Plan : SubscriptionPlans.Founding,
            SubscriptionStatus = SubscriptionStatus.Trial,
            TrialEndsAt = now.AddDays(Math.Clamp(cmd.TrialDays, 1, 365))
        };
        tenant.Use(clinic.Id);

        var roles = ClinicDefaults.Roles(clinic.Id);
        var admin = new User
        {
            ClinicId = clinic.Id, Email = email, FullName = cmd.AdminFullName.Trim(), Phone = clinic.Phone,
            Role = roles.First(r => r.Name == SystemRoles.Admin), PreferredLanguage = clinic.DefaultLanguage
        };
        admin.PasswordHash = hasher.HashPassword(admin, cmd.AdminPassword);

        var templates = ClinicDefaults.Templates(clinic.Id);
        db.Clinics.Add(clinic);
        db.ClinicSettings.Add(new ClinicSettings { ClinicId = clinic.Id });
        db.Roles.AddRange(roles);
        db.Users.Add(admin);
        db.Procedures.AddRange(ClinicDefaults.Procedures(clinic.Id));
        db.MessageTemplates.AddRange(templates);
        db.FollowUpRules.AddRange(ClinicDefaults.Rules(clinic.Id, templates));
        if (cmd.AdminIsDentist)
            db.Dentists.Add(new Dentist { ClinicId = clinic.Id, User = admin, FullName = admin.FullName, Phone = admin.Phone, Email = admin.Email });

        await db.SaveChangesAsync(ct);
        return new ProvisionedClinic(clinic.Id, admin.Id, clinic.Slug);
    }

    /// <summary>A URL-safe identifier from the Latin letters of the name, made unique with a random suffix.</summary>
    internal static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (c is >= 'A' and <= 'Z') builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var stem = builder.ToString().Trim('-');
        if (stem.Length > 60) stem = stem[..60].Trim('-');
        if (stem.Length == 0) stem = "clinic";
        return $"{stem}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
    }
}
