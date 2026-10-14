using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Features.Appointments;
using Tahona.Application.Features.Staff;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController(AppointmentService appointments) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.AppointmentsView)]
    public Task<IReadOnlyList<AppointmentDto>> List([FromQuery] AppointmentQuery query, CancellationToken ct) => appointments.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.AppointmentsView)]
    public Task<AppointmentDto> Get(Guid id, CancellationToken ct) => appointments.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.AppointmentsManage)]
    public Task<AppointmentDto> Create(AppointmentUpsertRequest request, CancellationToken ct) => appointments.CreateAsync(request, ct);

    [HttpPut("{id:guid}"), HasPermission(Permissions.AppointmentsManage)]
    public Task<AppointmentDto> Update(Guid id, AppointmentUpsertRequest request, CancellationToken ct) => appointments.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/reschedule"), HasPermission(Permissions.AppointmentsManage)]
    public Task<AppointmentDto> Reschedule(Guid id, RescheduleRequest request, CancellationToken ct) => appointments.RescheduleAsync(id, request, ct);

    [HttpPatch("{id:guid}/status"), HasPermission(Permissions.AppointmentsManage)]
    public Task<AppointmentDto> Status(Guid id, AppointmentStatusRequest request, CancellationToken ct) => appointments.ChangeStatusAsync(id, request, ct);
}

/// <summary>Reception's view of today: the list in time order, who is late, balances, and what is waiting.</summary>
[ApiController]
[Route("api/front-desk")]
public class FrontDeskController(FrontDeskService frontDesk) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.AppointmentsView)]
    public Task<FrontDeskDto> Get([FromQuery] Guid? dentistId, CancellationToken ct) => frontDesk.GetAsync(dentistId, ct);
}

[ApiController]
[Route("api/dentists")]
public class DentistsController(DentistService dentists) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.DashboardView)]
    public Task<IReadOnlyList<DentistDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => dentists.ListAsync(includeInactive, ct);

    [HttpPost, HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Create(DentistUpsertRequest request, CancellationToken ct) => Ok(new { id = await dentists.CreateAsync(request, ct) });

    [HttpPut("{id:guid}"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Update(Guid id, DentistUpsertRequest request, CancellationToken ct)
    {
        await dentists.UpdateAsync(id, request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/staff")]
public class StaffController(StaffService staff) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.StaffManage)]
    public Task<IReadOnlyList<StaffUserDto>> List(CancellationToken ct) => staff.ListAsync(ct);

    [HttpPost, HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken ct) => Ok(new { id = await staff.CreateAsync(request, ct) });

    [HttpPut("{id:guid}"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> Update(Guid id, UpdateStaffRequest request, CancellationToken ct)
    {
        await staff.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await staff.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("roles"), HasPermission(Permissions.StaffManage)]
    public Task<IReadOnlyList<RoleDto>> Roles(CancellationToken ct) => staff.ListRolesAsync(ct);

    [HttpPut("roles/{id:guid}"), HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> UpdateRole(Guid id, UpdateRoleRequest request, CancellationToken ct)
    {
        await staff.UpdateRoleAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("permissions"), HasPermission(Permissions.StaffManage)]
    public IReadOnlyList<string> AllPermissions() => Permissions.All;
}
