using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Clinical;
using Tahona.Application.Features.Documents;
using Tahona.Application.Features.Patients;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Authorization;
using Tahona.Domain.Enums;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController(
    PatientService patients,
    DentalChartService chart,
    ClinicalRecordsService clinical,
    DocumentService documents,
    RecoveryService recovery) : ControllerBase
{
    // ---------------------------------------------------------------- core

    [HttpGet, HasPermission(Permissions.PatientsView)]
    public Task<PagedResult<PatientListItemDto>> List([FromQuery] PatientListQuery query, CancellationToken ct) => patients.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.PatientsView)]
    public Task<PatientDetailDto> Get(Guid id, CancellationToken ct) => patients.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.PatientsCreate)]
    public async Task<IActionResult> Create(PatientUpsertRequest request, CancellationToken ct)
    {
        var id = await patients.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.PatientsEdit)]
    public async Task<IActionResult> Update(Guid id, PatientUpsertRequest request, CancellationToken ct)
    {
        await patients.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.PatientsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await patients.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/timeline"), HasPermission(Permissions.PatientsView)]
    public Task<IReadOnlyList<TimelineEventDto>> Timeline(Guid id, CancellationToken ct) => patients.GetTimelineAsync(id, ct);

    [HttpGet("{id:guid}/recovery"), HasPermission(Permissions.RecoveryView)]
    public Task<RecoveryPatientDto?> Recovery(Guid id, CancellationToken ct) => recovery.GetPatientAsync(id, ct);

    // ---------------------------------------------------------------- staff notes

    [HttpGet("{id:guid}/notes"), HasPermission(Permissions.PatientsView)]
    public Task<IReadOnlyList<PatientNoteDto>> Notes(Guid id, CancellationToken ct) => patients.ListNotesAsync(id, ct);

    [HttpPost("{id:guid}/notes"), HasPermission(Permissions.PatientsEdit)]
    public async Task<IActionResult> AddNote(Guid id, PatientNoteRequest request, CancellationToken ct) =>
        Ok(new { id = await patients.AddNoteAsync(id, request, ct) });

    [HttpDelete("{id:guid}/notes/{noteId:guid}"), HasPermission(Permissions.PatientsEdit)]
    public async Task<IActionResult> DeleteNote(Guid id, Guid noteId, CancellationToken ct)
    {
        await patients.DeleteNoteAsync(id, noteId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- dental chart

    [HttpGet("{id:guid}/chart"), HasPermission(Permissions.ChartView)]
    public Task<DentalChartDto> Chart(Guid id, CancellationToken ct) => chart.GetChartAsync(id, ct);

    [HttpGet("{id:guid}/chart/teeth/{tooth:int}"), HasPermission(Permissions.ChartView)]
    public Task<IReadOnlyList<ToothHistoryEntryDto>> ToothHistory(Guid id, int tooth, CancellationToken ct) => chart.GetToothHistoryAsync(id, tooth, ct);

    [HttpPut("{id:guid}/chart/teeth/{tooth:int}/notes"), HasPermission(Permissions.ChartEdit)]
    public async Task<IActionResult> ToothNote(Guid id, int tooth, ToothNoteRequest request, CancellationToken ct)
    {
        await chart.SetToothNoteAsync(id, tooth, request, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/chart/conditions"), HasPermission(Permissions.ChartEdit)]
    public Task<DentalConditionDto> AddCondition(Guid id, DentalConditionRequest request, CancellationToken ct) => chart.AddConditionAsync(id, request, ct);

    [HttpPut("{id:guid}/chart/conditions/{conditionId:guid}"), HasPermission(Permissions.ChartEdit)]
    public async Task<IActionResult> UpdateCondition(Guid id, Guid conditionId, DentalConditionRequest request, CancellationToken ct)
    {
        await chart.UpdateConditionAsync(id, conditionId, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/chart/conditions/{conditionId:guid}"), HasPermission(Permissions.ChartEdit)]
    public async Task<IActionResult> DeleteCondition(Guid id, Guid conditionId, CancellationToken ct)
    {
        await chart.DeleteConditionAsync(id, conditionId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- treatment history

    [HttpGet("{id:guid}/treatments"), HasPermission(Permissions.TreatmentView)]
    public Task<IReadOnlyList<TreatmentRecordDto>> Treatments(Guid id, CancellationToken ct) => clinical.ListTreatmentsAsync(id, ct);

    [HttpPost("{id:guid}/treatments"), HasPermission(Permissions.TreatmentManage)]
    public async Task<IActionResult> AddTreatment(Guid id, TreatmentRecordRequest request, CancellationToken ct) =>
        Ok(new { id = await clinical.AddTreatmentAsync(id, request, ct) });

    // ---------------------------------------------------------------- clinical notes

    [HttpGet("{id:guid}/clinical-notes"), HasPermission(Permissions.ClinicalView)]
    public Task<IReadOnlyList<ClinicalNoteDto>> ClinicalNotes(Guid id, CancellationToken ct) => clinical.ListNotesAsync(id, ct);

    [HttpPost("{id:guid}/clinical-notes"), HasPermission(Permissions.ClinicalManage)]
    public async Task<IActionResult> AddClinicalNote(Guid id, ClinicalNoteRequest request, CancellationToken ct) =>
        Ok(new { id = await clinical.AddNoteAsync(id, request, ct) });

    [HttpPut("{id:guid}/clinical-notes/{noteId:guid}"), HasPermission(Permissions.ClinicalManage)]
    public async Task<IActionResult> UpdateClinicalNote(Guid id, Guid noteId, ClinicalNoteRequest request, CancellationToken ct)
    {
        await clinical.UpdateNoteAsync(id, noteId, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/clinical-notes/{noteId:guid}"), HasPermission(Permissions.ClinicalManage)]
    public async Task<IActionResult> DeleteClinicalNote(Guid id, Guid noteId, CancellationToken ct)
    {
        await clinical.DeleteNoteAsync(id, noteId, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- documents

    [HttpGet("{id:guid}/documents"), HasPermission(Permissions.DocumentsView)]
    public Task<IReadOnlyList<DocumentDto>> Documents(Guid id, [FromQuery] DocumentCategory? category, CancellationToken ct) =>
        documents.ListAsync(id, category, ct);

    [HttpPost("{id:guid}/documents"), HasPermission(Permissions.DocumentsManage)]
    [RequestSizeLimit(DocumentService.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentService.MaxBytes + 1024 * 1024)]
    public async Task<DocumentDto> Upload(Guid id, [FromForm] IFormFile file, [FromForm] DocumentCategory category,
        [FromForm] string? description, [FromForm] int? toothNumber, CancellationToken ct)
    {
        if (file is null) throw RequestValidationException.For("file", "Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        return await documents.UploadAsync(new UploadDocumentCommand(id, category, file.FileName, file.ContentType, file.Length, stream,
            description, toothNumber), ct);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download"), HasPermission(Permissions.DocumentsView)]
    public async Task<IActionResult> Download(Guid id, Guid documentId, [FromQuery] bool inline, CancellationToken ct)
    {
        var file = await documents.DownloadAsync(id, documentId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return inline ? File(file.Content, file.ContentType) : File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}"), HasPermission(Permissions.DocumentsManage)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        await documents.DeleteAsync(id, documentId, ct);
        return NoContent();
    }
}
