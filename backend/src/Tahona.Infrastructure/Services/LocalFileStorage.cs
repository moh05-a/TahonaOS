using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Services;

public class StorageOptions
{
    public const string Section = "Storage";
    public string Root { get; set; } = "storage";
}

/// <summary>
/// Stores patient files on local disk under {root}/{clinic}/{patient}/{random}_{name}. Files are never served
/// statically; downloads go through an authorised, tenant-filtered endpoint. Swap for S3/Azure Blob in production.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.Root);

    public async Task<string> SaveAsync(Guid clinicId, Guid patientId, string fileName, Stream content, CancellationToken ct)
    {
        var relative = Path.Combine(clinicId.ToString("N"), patientId.ToString("N"), $"{Guid.NewGuid():N}_{fileName}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return relative.Replace('\\', '/');
    }

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var full = Resolve(storagePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Invalid storage path.");
        return full;
    }
}
