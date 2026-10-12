using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Billing;
using Tahona.Application.Features.Clinical;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/treatment-plans")]
public class TreatmentPlansController(TreatmentPlanService plans, InvoiceService invoices) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.TreatmentView)]
    public Task<PagedResult<TreatmentPlanListItemDto>> List([FromQuery] TreatmentPlanQuery query, CancellationToken ct) => plans.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.TreatmentView)]
    public Task<TreatmentPlanDetailDto> Get(Guid id, CancellationToken ct) => plans.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> Create(TreatmentPlanUpsertRequest request, CancellationToken ct)
    {
        var id = await plans.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> Update(Guid id, TreatmentPlanUpsertRequest request, CancellationToken ct)
    {
        await plans.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status"), HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> Status(Guid id, PlanStatusRequest request, CancellationToken ct)
    {
        await plans.ChangeStatusAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/items/{itemId:guid}/complete"), HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> CompleteItem(Guid id, Guid itemId, CompleteItemRequest request, CancellationToken ct)
    {
        await plans.CompleteItemAsync(id, itemId, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/items/{itemId:guid}/cancel"), HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> CancelItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await plans.CancelItemAsync(id, itemId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/invoice"), HasPermission(Permissions.InvoicesManage)]
    public async Task<IActionResult> Invoice(Guid id, InvoiceFromPlanRequest request, CancellationToken ct) =>
        Ok(new { id = await invoices.CreateFromPlanAsync(id, request, ct) });
}

[ApiController]
[Route("api/procedures")]
public class ProceduresController(ProcedureService procedures) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.DashboardView)]
    public Task<IReadOnlyList<ProcedureDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => procedures.ListAsync(includeInactive, ct);

    [HttpPost, HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Create(ProcedureUpsertRequest request, CancellationToken ct) => Ok(new { id = await procedures.CreateAsync(request, ct) });

    [HttpPut("{id:guid}"), HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Update(Guid id, ProcedureUpsertRequest request, CancellationToken ct)
    {
        await procedures.UpdateAsync(id, request, ct);
        return NoContent();
    }
}
