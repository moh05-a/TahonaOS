using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Documents;

public record DocumentDto(Guid Id, Guid PatientId, DocumentCategory Category, string FileName, string ContentType, long SizeBytes,
    string? Description, int? ToothNumber, string? UploadedBy, DateTime CreatedAt);

public record UploadDocumentCommand(Guid PatientId, DocumentCategory Category, string FileName, string ContentType, long SizeBytes,
    Stream Content, string? Description, int? ToothNumber);

public record DocumentDownload(Stream Content, string FileName, string ContentType);

public class DocumentService(IAppDbContext db, IFileStorage storage, ITenantContext tenant, ICurrentUser currentUser)
{
    public const long MaxBytes = 25 * 1024 * 1024;

    private static readonly Dictionary<string, string[]> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/webp"] = [".webp"],
        ["application/pdf"] = [".pdf"],
        ["application/dicom"] = [".dcm"],
        ["application/octet-stream"] = [".dcm", ".stl"],
        ["model/stl"] = [".stl"]
    };

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid patientId, DocumentCategory? category, CancellationToken ct)
    {
        var q = db.Documents.AsNoTracking().Where(d => d.PatientId == patientId);
        if (category is not null) q = q.Where(d => d.Category == category);
        return await q.OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(d.Id, d.PatientId, d.Category, d.FileName, d.ContentType, d.SizeBytes, d.Description,
                d.ToothNumber, d.UploadedBy != null ? d.UploadedBy.FullName : null, d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<DocumentDto> UploadAsync(UploadDocumentCommand cmd, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == cmd.PatientId, ct)) throw new NotFoundException("Patient", cmd.PatientId);
        if (cmd.SizeBytes <= 0) throw RequestValidationException.For("file", "The file is empty.");
        if (cmd.SizeBytes > MaxBytes) throw RequestValidationException.For("file", "Files must be 25 MB or smaller.");

        var extension = Path.GetExtension(cmd.FileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(cmd.ContentType, out var extensions) || !extensions.Contains(extension))
            throw RequestValidationException.For("file", "Allowed file types: JPG, PNG, WEBP, PDF, DICOM (.dcm), STL.");

        var safeName = SanitizeFileName(cmd.FileName);
        var clinicId = tenant.ClinicId ?? throw new UnauthorizedException();
        var path = await storage.SaveAsync(clinicId, cmd.PatientId, safeName, cmd.Content, ct);

        var doc = new PatientDocument
        {
            PatientId = cmd.PatientId,
            Category = cmd.Category,
            FileName = safeName,
            StoragePath = path,
            ContentType = cmd.ContentType,
            SizeBytes = cmd.SizeBytes,
            Description = string.IsNullOrWhiteSpace(cmd.Description) ? null : cmd.Description.Trim(),
            ToothNumber = cmd.ToothNumber,
            UploadedById = currentUser.UserId
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        return new DocumentDto(doc.Id, doc.PatientId, doc.Category, doc.FileName, doc.ContentType, doc.SizeBytes, doc.Description,
            doc.ToothNumber, currentUser.FullName, doc.CreatedAt);
    }

    public async Task<DocumentDownload> DownloadAsync(Guid patientId, Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.PatientId == patientId, ct)
                  ?? throw new NotFoundException("Document", id);
        var stream = await storage.OpenReadAsync(doc.StoragePath, ct) ?? throw new NotFoundException("File", doc.FileName);
        return new DocumentDownload(stream, doc.FileName, doc.ContentType);
    }

    public async Task DeleteAsync(Guid patientId, Guid id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.PatientId == patientId, ct)
                  ?? throw new NotFoundException("Document", id);
        db.Documents.Remove(doc);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(doc.StoragePath, ct);
    }

    private static string SanitizeFileName(string name)
    {
        var file = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(file.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > 200) cleaned = cleaned[^200..];
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }
}
