using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

public record ConfirmStepRequest(bool Confirmed);

/// <summary>The switching checklist: what is set up, what is left, and the figures to verify against the previous system.</summary>
[ApiController]
[Route("api/onboarding")]
public class OnboardingController(OnboardingService onboarding) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.SettingsManage)]
    public Task<OnboardingStatusDto> Get(CancellationToken ct) => onboarding.GetAsync(ct);

    [HttpPut("steps/{key}"), HasPermission(Permissions.SettingsManage)]
    public async Task<OnboardingStatusDto> Confirm(string key, ConfirmStepRequest request, CancellationToken ct)
    {
        await onboarding.SetConfirmedAsync(key, request.Confirmed, ct);
        return await onboarding.GetAsync(ct);
    }

    [HttpPost("complete"), HasPermission(Permissions.SettingsManage)]
    public async Task<OnboardingStatusDto> Complete(CancellationToken ct)
    {
        await onboarding.CompleteAsync(ct);
        return await onboarding.GetAsync(ct);
    }

    [HttpPost("reopen"), HasPermission(Permissions.SettingsManage)]
    public async Task<OnboardingStatusDto> Reopen(CancellationToken ct)
    {
        await onboarding.ReopenAsync(ct);
        return await onboarding.GetAsync(ct);
    }
}
