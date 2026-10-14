using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tahona.Application.Features.Auth;
using Tahona.Application.Features.Provisioning;

namespace Tahona.Api.Controllers;

public record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserProfileDto User);

public record RefreshRequest(string? RefreshToken);

public record LanguageRequest(string Language);

public record ForgotPasswordRequest(string Email);

/// <summary>What the sign-in page may offer on this deployment.</summary>
public record AuthOptionsDto(bool PasswordResetByEmail);

/// <summary>
/// JWT access tokens (short-lived, kept in memory by the SPA) + rotating refresh tokens delivered in an
/// HttpOnly, SameSite=Strict cookie scoped to /api/auth.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, InvitationService invitations, AccountRecoveryService recovery, IWebHostEnvironment env)
    : ControllerBase
{
    private const string RefreshCookie = "tahona_rt";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, Ip, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return new LoginResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    public async Task<LoginResponse> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookie] ?? request?.RefreshToken ?? "";
        try
        {
            var result = await auth.RefreshAsync(token, Ip, ct);
            SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
            return new LoginResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
        }
        catch
        {
            ClearRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie] ?? request?.RefreshToken, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    /// <summary>What an invitation link is for, shown before the clinic owner creates the account.</summary>
    [HttpGet("invitation")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public Task<InvitationInfoDto> Invitation([FromQuery] string token, CancellationToken ct) => invitations.GetAsync(token, ct);

    /// <summary>Creates the clinic and its first administrator from an invitation, and signs them in.</summary>
    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<LoginResponse> AcceptInvitation(AcceptInvitationRequest request, CancellationToken ct)
    {
        var result = await invitations.AcceptAsync(request, Ip, ct);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return new LoginResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }

    [HttpGet("options")]
    [AllowAnonymous]
    public AuthOptionsDto Options() => new(recovery.SelfServiceAvailable);

    /// <summary>
    /// Asks for a password reset link by e-mail. Always answers the same way, whether or not the address has an account.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await recovery.RequestByEmailAsync(request.Email ?? "", ct);
        return NoContent();
    }

    /// <summary>Sets a new password from a single-use reset link.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordWithTokenRequest request, CancellationToken ct)
    {
        await recovery.ResetAsync(request, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<UserProfileDto> Me(CancellationToken ct) => auth.GetProfileAsync(ct);

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPut("language")]
    [Authorize]
    public async Task<IActionResult> SetLanguage(LanguageRequest request, CancellationToken ct)
    {
        await auth.SetLanguageAsync(request.Language, ct);
        return NoContent();
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps || !env.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires
    };

    private void SetRefreshCookie(string token, DateTime expires) =>
        Response.Cookies.Append(RefreshCookie, token, CookieOptions(new DateTimeOffset(expires, TimeSpan.Zero)));

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, CookieOptions(null));
}
