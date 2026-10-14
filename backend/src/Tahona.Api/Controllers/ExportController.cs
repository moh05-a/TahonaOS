using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Features.Export;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

/// <summary>
/// The clinic's own data, taken out in open formats. Stays available when the account is read-only, so a clinic
/// that stops paying can still leave with its records.
/// </summary>
[ApiController]
[Route("api/export")]
public class ExportController(DataExportService exports) : ControllerBase
{
    /// <summary>A ZIP of CSV files with a manifest. POST because every export is recorded in the audit log.</summary>
    [HttpPost("full"), HasPermission(Permissions.DataExport)]
    public async Task<IActionResult> Full([FromQuery] bool includeDocuments, CancellationToken ct)
    {
        var export = await exports.ExportAsync(includeDocuments, ct);
        return File(export.Content, "application/zip", export.FileName);
    }
}
