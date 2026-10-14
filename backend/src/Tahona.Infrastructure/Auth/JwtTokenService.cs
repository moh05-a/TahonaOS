using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tahona.Application.Common;
using Tahona.Domain.Entities;

namespace Tahona.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    /// <summary>HMAC signing key (≥ 32 chars). Supplied via env var JWT__SECRET — never committed.</summary>
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "tahona";
    public string Audience { get; set; } = "tahona-app";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public static class TahonaClaims
{
    public const string ClinicId = "clinic_id";
    public const string Permission = "perm";
    public const string DentistId = "dentist_id";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(User user, Role role, Clinic clinic, Guid? dentistId)
    {
        var expires = clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(TahonaClaims.ClinicId, clinic.Id.ToString()),
            new(ClaimTypes.Role, role.Name)
        };
        if (dentistId is not null) claims.Add(new Claim(TahonaClaims.DentistId, dentistId.Value.ToString()));
        claims.AddRange(role.EffectivePermissions.Select(p => new Claim(TahonaClaims.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, clock.UtcNow, expires,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return new GeneratedRefreshToken(raw, HashToken(raw), clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    public string HashToken(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
