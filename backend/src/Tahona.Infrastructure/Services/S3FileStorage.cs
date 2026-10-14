using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Services;

public class S3Options
{
    /// <summary>Bucket that holds patient files (env STORAGE__S3__BUCKET). It must be private.</summary>
    public string? Bucket { get; set; }
    public string Region { get; set; } = "us-east-1";
    /// <summary>Endpoint of an S3-compatible service (MinIO, Cloudflare R2, DigitalOcean Spaces). Empty for AWS.</summary>
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    /// <summary>Path-style addressing, needed by MinIO and some compatible services.</summary>
    public bool ForcePathStyle { get; set; }
}

/// <summary>
/// Stores patient files in an S3-compatible bucket under {clinic}/{patient}/{random}_{name}. Objects are private and
/// encrypted at rest by the service; they are only ever read through the API's authorised, tenant-filtered endpoint.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        var s3 = options.Value.S3;
        _bucket = s3.Bucket ?? throw new InvalidOperationException("Storage:S3:Bucket is required when Storage:Provider is s3.");
        var config = new AmazonS3Config { ForcePathStyle = s3.ForcePathStyle };
        if (string.IsNullOrWhiteSpace(s3.ServiceUrl)) config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3.Region);
        else
        {
            config.ServiceURL = s3.ServiceUrl;
            config.AuthenticationRegion = s3.Region;
        }
        // Explicit keys when given; otherwise the SDK's default chain (instance role, environment).
        _client = string.IsNullOrWhiteSpace(s3.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(s3.AccessKey, s3.SecretKey), config);
    }

    public string Name => $"S3 bucket {_bucket}";

    public async Task<string> SaveAsync(Guid clinicId, Guid patientId, string fileName, Stream content, CancellationToken ct)
    {
        var key = $"{clinicId:N}/{patientId:N}/{Guid.NewGuid():N}_{fileName}";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket, Key = key, InputStream = content, AutoCloseStream = false,
            // Compatible services differ in checksum support; the request is still signed and sent over TLS.
            DisablePayloadSigning = false
        }, ct);
        return key;
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, Validate(storagePath), ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => _client.DeleteObjectAsync(_bucket, Validate(storagePath), ct);

    /// <summary>Keys are generated here; anything that looks like a traversal is refused.</summary>
    private static string Validate(string key) =>
        key.Contains("..") || key.StartsWith('/') || key.Contains('\\') ? throw new UnauthorizedAccessException("Invalid storage path.") : key;

    public void Dispose() => _client.Dispose();
}
