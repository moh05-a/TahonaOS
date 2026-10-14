using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Application.Features.Auth;
using Tahona.Infrastructure.Services;

namespace Tahona.Tests;

/// <summary>A test that needs a real external service. Skipped, visibly, unless the named environment variable is set.</summary>
public sealed class ServiceFactAttribute : FactAttribute
{
    public ServiceFactAttribute(string variable, string what)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))) Skip = $"Set {variable} ({what}) to run this test.";
    }
}

/// <summary>
/// Storage and e-mail against real servers, so the adapters are exercised and not only their fakes.
///   TAHONA_TEST_S3   = "serviceUrl|accessKey|secretKey"   e.g. a local MinIO: http://localhost:9000|user|password
///   TAHONA_TEST_SMTP = "host:smtpPort:httpPort"           a local Mailpit: localhost:1025:8025
/// </summary>
public class ExternalServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [ServiceFact("TAHONA_TEST_S3", "an S3-compatible endpoint")]
    public async Task Files_round_trip_through_an_s3_compatible_bucket_and_stay_inside_their_clinic_prefix()
    {
        var parts = Environment.GetEnvironmentVariable("TAHONA_TEST_S3")!.Split('|');
        var bucket = $"tahona-test-{Guid.NewGuid():N}"[..30];
        using var admin = new AmazonS3Client(new BasicAWSCredentials(parts[1], parts[2]),
            new AmazonS3Config { ServiceURL = parts[0], ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await admin.PutBucketAsync(bucket, None);
        try
        {
            using var storage = new S3FileStorage(Options.Create(new StorageOptions
            {
                Provider = "s3", S3 = new S3Options { Bucket = bucket, ServiceUrl = parts[0], AccessKey = parts[1], SecretKey = parts[2], ForcePathStyle = true }
            }));
            var clinic = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var content = Encoding.UTF8.GetBytes("صورة أشعة — x-ray bytes");

            var path = await storage.SaveAsync(clinic, patient, "xray.png", new MemoryStream(content), None);
            Assert.StartsWith($"{clinic:N}/{patient:N}/", path);
            Assert.EndsWith("_xray.png", path);

            await using (var read = await storage.OpenReadAsync(path, None))
            {
                Assert.NotNull(read);
                using var buffer = new MemoryStream();
                await read.CopyToAsync(buffer);
                Assert.Equal(content, buffer.ToArray());
            }

            Assert.Null(await storage.OpenReadAsync($"{clinic:N}/{patient:N}/missing.png", None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => storage.OpenReadAsync("../other-bucket/secret", None));

            await storage.DeleteAsync(path, None);
            Assert.Null(await storage.OpenReadAsync(path, None));
            Assert.Contains(bucket, storage.Name);
        }
        finally
        {
            await admin.DeleteBucketAsync(bucket, None);
        }
    }

    [ServiceFact("TAHONA_TEST_SMTP", "an SMTP test server with the Mailpit HTTP API")]
    public async Task Account_email_is_delivered_through_smtp_with_arabic_text_intact()
    {
        var parts = Environment.GetEnvironmentVariable("TAHONA_TEST_SMTP")!.Split(':');
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = parts[0], Port = int.Parse(parts[1]), From = "TahonaOS <no-reply@tahona.test>", Security = "none"
        }), NullLogger<SmtpEmailSender>.Instance);
        Assert.True(sender.IsConfigured);

        var to = $"owner-{Guid.NewGuid():N}@clinic.test";
        var link = $"https://app.example.test/welcome?token={Guid.NewGuid():N}";
        Assert.True(await sender.SendAsync(AccountEmails.Invitation(to, link, "عيادة النور", 30, DateTime.UtcNow.AddDays(14)), None));

        using var http = new HttpClient { BaseAddress = new Uri($"http://{parts[0]}:{parts[2]}") };
        var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query=to:{to}");
        var summary = Assert.Single(search.GetProperty("messages").EnumerateArray());
        var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}");
        var body = message.GetProperty("Text").GetString()!;
        Assert.Contains(link, body);
        Assert.Contains("عيادة النور", body);
        Assert.Contains("no-reply@tahona.test", message.GetProperty("From").GetProperty("Address").GetString());

        // A server that is not there is reported as a failure, not an exception.
        var unreachable = new SmtpEmailSender(Options.Create(new EmailOptions { Host = "127.0.0.1", Port = 1, From = "x@y.test", Security = "none" }),
            NullLogger<SmtpEmailSender>.Instance);
        Assert.False(await unreachable.SendAsync(new EmailMessage(to, "s", "b"), None));
        Assert.False(new SmtpEmailSender(Options.Create(new EmailOptions()), NullLogger<SmtpEmailSender>.Instance).IsConfigured);
    }
}
