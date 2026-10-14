using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Infrastructure.Messaging;

public class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    /// <summary>
    /// Deployment-wide Cloud API token (env WHATSAPP__ACCESSTOKEN). For a single-clinic installation only: a clinic
    /// that connects its own number under Settings uses that instead.
    /// </summary>
    public string? AccessToken { get; set; }
    /// <summary>Deployment-wide business phone number id (env WHATSAPP__PHONENUMBERID).</summary>
    public string? PhoneNumberId { get; set; }
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Token Meta echoes during webhook verification (env WHATSAPP__WEBHOOKVERIFYTOKEN).</summary>
    public string? WebhookVerifyToken { get; set; }
    /// <summary>App secret used to validate X-Hub-Signature-256 on webhooks (env WHATSAPP__APPSECRET).</summary>
    public string? AppSecret { get; set; }

    public bool HasCloudApiCredentials => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}

/// <summary>
/// Used when no API credentials exist: returns a wa.me deep link with the rendered text so a staff member sends it
/// from WhatsApp. Nothing is sent by the system, and the result says so.
/// </summary>
public sealed class ManualWhatsAppProvider : IWhatsAppProvider
{
    public string Name => "manual";
    public bool IsConfigured => false;

    public Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken ct)
    {
        var url = $"https://wa.me/{request.ToInternationalNumber}?text={Uri.EscapeDataString(request.Body)}";
        return Task.FromResult(new WhatsAppSendResult(false, null, null, true, url));
    }
}

/// <summary>
/// Calls to the Meta WhatsApp Business Cloud API for one set of credentials. Business-initiated messages outside the
/// 24-hour service window must use an approved template: the rendered variables are sent as body parameters in order.
/// </summary>
public sealed class MetaWhatsAppClient(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaWhatsAppClient> logger) : IWhatsAppAccountVerifier
{
    private string Base => $"https://graph.facebook.com/{options.Value.ApiVersion}";

    public async Task<WhatsAppSendResult> SendAsync(string phoneNumberId, string accessToken, WhatsAppSendRequest request, CancellationToken ct)
    {
        object payload = string.IsNullOrWhiteSpace(request.ProviderTemplateName)
            ? new { messaging_product = "whatsapp", to = request.ToInternationalNumber, type = "text", text = new { preview_url = false, body = request.Body } }
            : new
            {
                messaging_product = "whatsapp",
                to = request.ToInternationalNumber,
                type = "template",
                template = new
                {
                    name = request.ProviderTemplateName,
                    language = new { code = request.Language == "ar" ? "ar" : "en" },
                    components = request.TemplateParameters is { Count: > 0 }
                        ? new object[] { new { type = "body", parameters = request.TemplateParameters.Select(p => new { type = "text", text = p }).ToArray() } }
                        : []
                }
            };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}/messages") { Content = JsonContent.Create(payload) };
        message.Headers.Authorization = new("Bearer", accessToken);

        try
        {
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("WhatsApp send failed {Status}: {Body}", response.StatusCode, body.Length > 500 ? body[..500] : body);
                return new WhatsAppSendResult(false, null, ExtractError(body) ?? $"HTTP {(int)response.StatusCode}", false, null);
            }
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
            return new WhatsAppSendResult(true, id, null, false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogError(ex, "WhatsApp send failed");
            return new WhatsAppSendResult(false, null, "WhatsApp service unreachable.", false, null);
        }
    }

    /// <summary>Asks Meta for the number behind a phone number id, which proves the token can use it.</summary>
    public async Task<(WhatsAppNumberInfo? Number, string? Error)> VerifyAsync(string phoneNumberId, string accessToken, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name");
        message.Headers.Authorization = new("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return (null, ExtractError(body) ?? $"HTTP {(int)response.StatusCode}");
            using var doc = JsonDocument.Parse(body);
            string? Read(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (new WhatsAppNumberInfo(phoneNumberId, Read("display_phone_number"), Read("verified_name")), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(ex, "WhatsApp credential check failed");
            return (null, "WhatsApp could not be reached. Try again in a moment.");
        }
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// Chooses how the current clinic sends WhatsApp messages, once per unit of work:
///  1. the clinic's own connected number, if it has one;
///  2. otherwise the deployment-wide credentials, if set (single-clinic installations);
///  3. otherwise manual sending.
/// </summary>
public sealed class TenantWhatsAppProvider(AppDbContext db, MetaWhatsAppClient meta, ISecretProtector secrets, IOptions<WhatsAppOptions> options,
    ILogger<TenantWhatsAppProvider> logger) : IWhatsAppProvider
{
    private readonly ManualWhatsAppProvider _manual = new();
    private (string PhoneNumberId, string Token)? _credentials;
    private string _name = "manual";
    private bool _resolved;

    public string Name { get { Resolve(); return _name; } }
    public bool IsConfigured { get { Resolve(); return _credentials is not null; } }

    public Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken ct)
    {
        Resolve();
        return _credentials is { } c ? meta.SendAsync(c.PhoneNumberId, c.Token, request, ct) : _manual.SendAsync(request, ct);
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        // Tenant-filtered: only the current clinic's connection can be found.
        var own = db.ClinicWhatsApps.AsNoTracking().FirstOrDefault(w => w.IsEnabled);
        if (own is not null)
        {
            var token = secrets.Unprotect(own.EncryptedAccessToken);
            if (token is not null)
            {
                _credentials = (own.PhoneNumberId, token);
                _name = "whatsapp-cloud-api";
                return;
            }
            // The stored token cannot be read (key changed). Fall back to manual rather than to another clinic's number.
            logger.LogError("The WhatsApp token of clinic {ClinicId} could not be decrypted; messages fall back to manual sending.", own.ClinicId);
            return;
        }

        var shared = options.Value;
        if (shared.HasCloudApiCredentials)
        {
            _credentials = (shared.PhoneNumberId!, shared.AccessToken!);
            _name = "whatsapp-cloud-api (shared)";
        }
    }
}
