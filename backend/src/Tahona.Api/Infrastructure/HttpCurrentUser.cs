using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Tahona.Application.Common;
using Tahona.Infrastructure.Auth;

namespace Tahona.Api.Infrastructure;

/// <summary>Reads the caller from the validated JWT. In background jobs there is no HttpContext and the user is anonymous.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Find(JwtRegisteredClaimNames.Sub) ?? Find(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public Guid? ClinicId => Guid.TryParse(Find(TahonaClaims.ClinicId), out var id) ? id : null;

    public string? FullName => Find(JwtRegisteredClaimNames.Name) ?? Find(ClaimTypes.Name);

    public string? Role => Find(ClaimTypes.Role);

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(TahonaClaims.Permission).Select(c => c.Value).ToHashSet() ?? [];

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool HasPermission(string permission) => Principal?.HasClaim(TahonaClaims.Permission, permission) == true;

    private string? Find(string type) => Principal?.FindFirst(type)?.Value;
}
