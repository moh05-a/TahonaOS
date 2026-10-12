using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/recovery")]
public class RecoveryController(RecoveryService recovery, RecoverySyncService sync, FollowUpRuleService rules) : ControllerBase
{
    /// <summary>The morning view: who to contact today and what they are worth.</summary>
    [HttpGet("dashboard"), HasPermission(Permissions.RecoveryView)]
    public Task<RecoveryDashboardDto> Dashboard([FromQuery] RecoveryQuery query, CancellationToken ct) => recovery.GetDashboardAsync(query, ct);

    [HttpGet("patients/{patientId:guid}"), HasPermission(Permissions.RecoveryView)]
    public async Task<IActionResult> Patient(Guid patientId, CancellationToken ct) =>
        await recovery.GetPatientAsync(patientId, ct) is { } p ? Ok(p) : NoContent();

    [HttpPost("patients/{patientId:guid}/contact"), HasPermission(Permissions.RecoveryManage)]
    public Task<ContactPatientResult> Contact(Guid patientId, ContactPatientRequest request, CancellationToken ct) =>
        recovery.ContactAsync(patientId, request, ct);

    [HttpPost("patients/{patientId:guid}/outcome"), HasPermission(Permissions.RecoveryManage)]
    public async Task<IActionResult> Outcome(Guid patientId, OutcomeRequest request, CancellationToken ct)
    {
        await recovery.RecordOutcomeAsync(patientId, request, ct);
        return NoContent();
    }

    /// <summary>Runs the follow-up automation now (it also runs in the background every 15 minutes).</summary>
    [HttpPost("sync"), HasPermission(Permissions.RecoveryManage)]
    public Task<SyncResult> Sync(CancellationToken ct) => sync.SyncAsync(ct);

    [HttpGet("follow-ups"), HasPermission(Permissions.RecoveryView)]
    public Task<PagedResult<FollowUpTaskDto>> FollowUps([FromQuery] FollowUpTaskQuery query, CancellationToken ct) => rules.ListTasksAsync(query, ct);

    [HttpGet("rules"), HasPermission(Permissions.RecoveryView)]
    public Task<IReadOnlyList<FollowUpRuleDto>> Rules(CancellationToken ct) => rules.ListAsync(ct);

    [HttpPost("rules"), HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> CreateRule(FollowUpRuleRequest request, CancellationToken ct) => Ok(new { id = await rules.CreateAsync(request, ct) });

    [HttpPut("rules/{id:guid}"), HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateRule(Guid id, FollowUpRuleRequest request, CancellationToken ct)
    {
        await rules.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("rules/{id:guid}"), HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        await rules.DeleteAsync(id, ct);
        return NoContent();
    }
}
