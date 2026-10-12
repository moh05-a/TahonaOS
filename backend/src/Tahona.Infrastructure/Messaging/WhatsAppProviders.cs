using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Messaging;

public class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    /// <summary>Meta WhatsApp Cloud API permanent/system-user token (env WHATSAPP__ACCESSTOKEN).</summary>
    public string? AccessToken { get; set; }
    /// <summary>Business phone number id (env WHATSAPP__PHONENUMBERID).</summary>
    public string? PhoneNumberId { get; set; }
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Token Meta echoes during webhook verification (env WHATSAPP__WEBHOOKVERIFYTOKEN).</summary>
    public string? WebhookVerifyToken { get; set; }
    /// <summary>App secret used to validate X-Hub-Signature-256 on webhooks (env WHATSAPP__APPSECRET).</summary>
    public string? AppSecret { get; set; }

    public bool HasCloudApiCredentials => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}

/// <summary>
/// Default provider when no API credentials exist: returns a wa.me deep link with the rendered text so staff send it
/// from WhatsApp Web/Business in one click. Fully functional without any third-party account.
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
/// Meta WhatsApp Business Cloud API. Business-initiated messages outside the 24-hour service window must use an
/// approved template: set the template's ProviderTemplateName in Settings and the rendered body is sent as the
/// single body parameter list in variable order. Otherwise a free-form text message is sent.
/// </summary>
public sealed class MetaCloudWhatsAppProvider(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaCloudWhatsAppProvider> logger)
    : IWhatsAppProvider
{
    private readonly WhatsAppOptions _options = options.Value;

    public string Name => "meta-cloud-api";
    public bool IsConfigured => _options.HasCloudApiCredentials;

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken ct)
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

        using var message = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_options.ApiVersion}/{_options.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Authorization = new("Bearer", _options.AccessToken);

        try
        {
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("WhatsApp send failed {Status}: {Body}", response.StatusCode, body);
                return new WhatsAppSendResult(false, null, ExtractError(body) ?? $"HTTP {(int)response.StatusCode}", false, null);
            }
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
            return new WhatsAppSendResult(true, id, null, false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            logger.LogError(ex, "WhatsApp send failed");
            return new WhatsAppSendResult(false, null, "WhatsApp service unreachable.", false, null);
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
