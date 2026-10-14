using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Import;
using Tahona.Domain.Authorization;
using Tahona.Domain.Enums;

namespace Tahona.Api.Controllers;

/// <summary>
/// The import centre: upload a CSV or Excel file, map its columns, preview the result without changing anything,
/// then import. Restricted to staff with the data.import permission (administrators by default).
/// </summary>
[ApiController]
[Route("api/import")]
public class ImportController(ImportService imports) : ControllerBase
{
    /// <summary>The kinds of import and the fields each accepts.</summary>
    [HttpGet("definitions"), HasPermission(Permissions.DataImport)]
    public IReadOnlyList<ImportDefinition> Definitions() => ImportDefinitions.All;

    /// <summary>An example file with the recognised column names, in English or Arabic.</summary>
    [HttpGet("templates/{kind}"), HasPermission(Permissions.DataImport)]
    public IActionResult Template(ImportKind kind, [FromQuery] string lang = "en") =>
        File(ImportDefinitions.Template(kind, lang == "ar" ? "ar" : "en"), "text/csv; charset=utf-8", $"tahona-{kind.ToString().ToLowerInvariant()}-template.csv");

    [HttpGet("batches"), HasPermission(Permissions.DataImport)]
    public Task<IReadOnlyList<ImportBatchDto>> List(CancellationToken ct) => imports.ListAsync(ct);

    [HttpGet("batches/{id:guid}"), HasPermission(Permissions.DataImport)]
    public Task<ImportUploadDto> Get(Guid id, CancellationToken ct) => imports.GetAsync(id, ct);

    [HttpPost("batches"), HasPermission(Permissions.DataImport)]
    [RequestSizeLimit(ImportService.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportService.MaxFileBytes + 1024 * 1024)]
    public async Task<ImportUploadDto> Upload([FromForm] IFormFile file, [FromForm] ImportKind kind, [FromForm] string? sourceSystem, CancellationToken ct)
    {
        if (file is null) throw RequestValidationException.For("file", "Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        return await imports.UploadAsync(kind, file.FileName, stream, sourceSystem, ct);
    }

    /// <summary>Dry run: checks every row and reports what an import would do. Writes no clinic records.</summary>
    [HttpPost("batches/{id:guid}/preview"), HasPermission(Permissions.DataImport)]
    public Task<ImportPreviewDto> Preview(Guid id, ImportRunRequest request, CancellationToken ct) => imports.PreviewAsync(id, request, ct);

    /// <summary>Applies the import in one transaction. Committing the same batch again is refused.</summary>
    [HttpPost("batches/{id:guid}/commit"), HasPermission(Permissions.DataImport)]
    public Task<ImportCommitDto> Commit(Guid id, ImportRunRequest request, CancellationToken ct) => imports.CommitAsync(id, request, ct);

    [HttpPost("batches/{id:guid}/undo"), HasPermission(Permissions.DataImport)]
    public Task<ImportUndoDto> Undo(Guid id, CancellationToken ct) => imports.UndoAsync(id, ct);

    /// <summary>Every source row with its result, as CSV.</summary>
    [HttpGet("batches/{id:guid}/report"), HasPermission(Permissions.DataImport)]
    public async Task<IActionResult> Report(Guid id, [FromQuery] bool problemsOnly, CancellationToken ct)
    {
        var (content, fileName) = await imports.ReportAsync(id, problemsOnly, ct);
        return File(content, "text/csv; charset=utf-8", fileName);
    }

    [HttpDelete("batches/{id:guid}"), HasPermission(Permissions.DataImport)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await imports.DeleteAsync(id, ct);
        return NoContent();
    }
}
