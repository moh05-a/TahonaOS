using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Infrastructure.Auth;
using Tahona.Application.Features.Ai;
using Tahona.Application.Features.Dashboard;
using Tahona.Application.Features.Search;
using Tahona.Application.Features.Settings;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <param name="mine">Dentists can limit the schedule to their own appointments.</param>
    [HttpGet, HasPermission(Permissions.DashboardView)]
    public Task<DashboardDto> Get([FromQuery] bool mine, CancellationToken ct)
    {
        Guid? dentistId = mine && Guid.TryParse(User.FindFirst(TahonaClaims.DentistId)?.Value, out var d) ? d : null;
        return dashboard.GetAsync(dentistId, ct);
    }
}

[ApiController]
[Route("api/reports")]
public class ReportsController(AnalyticsService analytics) : ControllerBase
{
    [HttpGet("analytics"), HasPermission(Permissions.ReportsView)]
    public Task<AnalyticsDto> Analytics([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.GetAsync(query, ct);
}

[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController(SearchService search) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.PatientsView)]
    public Task<SearchResultDto> Search([FromQuery] string? q, CancellationToken ct) => search.SearchAsync(q, ct);
}

[ApiController]
[Route("api/ai")]
public class AiController(AssistantService assistant, ClinicalNoteAiService notes) : ControllerBase
{
    [HttpGet("assistant/status"), HasPermission(Permissions.AiUse)]
    public AssistantStatusDto Status() => assistant.Status();

    [HttpPost("assistant"), HasPermission(Permissions.AiUse), EnableRateLimiting("ai")]
    public Task<AssistantAnswerDto> Ask(AssistantRequest request, CancellationToken ct) => assistant.AskAsync(request, ct);

    [HttpPost("clinical-note/structure"), HasPermission(Permissions.ClinicalManage), EnableRateLimiting("ai")]
    public Task<StructuredNoteDto> Structure(StructureNoteRequest request, CancellationToken ct) => notes.StructureAsync(request, ct);
}

[ApiController]
[Route("api/settings")]
public class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet("clinic"), Authorize]
    public Task<ClinicProfileDto> Clinic(CancellationToken ct) => settings.GetProfileAsync(ct);

    [HttpPut("clinic"), HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> UpdateClinic(ClinicProfileRequest request, CancellationToken ct)
    {
        await settings.UpdateProfileAsync(request, ct);
        return NoContent();
    }

    [HttpGet, Authorize]
    public Task<ClinicSettingsDto> Get(CancellationToken ct) => settings.GetSettingsAsync(ct);

    [HttpPut, HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Update(ClinicSettingsDto request, CancellationToken ct)
    {
        await settings.UpdateSettingsAsync(request, ct);
        return NoContent();
    }

    [HttpGet("integrations"), Authorize]
    public IntegrationStatusDto Integrations() => settings.Integrations();
}

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController(AuditLogService audit) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.AuditView)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] AuditLogQuery query, CancellationToken ct) => audit.ListAsync(query, ct);

    [HttpGet("entity-types"), HasPermission(Permissions.AuditView)]
    public Task<IReadOnlyList<string>> EntityTypes(CancellationToken ct) => audit.EntityTypesAsync(ct);
}
