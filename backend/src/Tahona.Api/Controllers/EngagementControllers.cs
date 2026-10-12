using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Messaging;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Messaging;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/messages")]
public class MessagesController(MessagingService messaging) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.MessagesView)]
    public Task<PagedResult<MessageDto>> List([FromQuery] MessageQuery query, CancellationToken ct) => messaging.ListAsync(query, ct);

    [HttpGet("conversations"), HasPermission(Permissions.MessagesView)]
    public Task<IReadOnlyList<ConversationDto>> Conversations([FromQuery] int take = 50, CancellationToken ct = default) =>
        messaging.ConversationsAsync(take, ct);

    [HttpGet("status"), HasPermission(Permissions.MessagesView)]
    public MessagingStatusDto Status() => messaging.Status();

    [HttpPost("render"), HasPermission(Permissions.MessagesSend)]
    public Task<RenderedMessage> Render(RenderRequest request, CancellationToken ct) => messaging.RenderAsync(request, ct);

    [HttpPost("send"), HasPermission(Permissions.MessagesSend)]
    public Task<SendMessageResult> Send(SendMessageRequest request, CancellationToken ct) => messaging.SendAsync(request, ct);

    [HttpPost("{id:guid}/mark-sent"), HasPermission(Permissions.MessagesSend)]
    public async Task<IActionResult> MarkSent(Guid id, CancellationToken ct)
    {
        await messaging.MarkSentAsync(id, ct);
        return NoContent();
    }

    [HttpGet("templates"), HasPermission(Permissions.MessagesView)]
    public Task<IReadOnlyList<MessageTemplateDto>> Templates(CancellationToken ct) => messaging.ListTemplatesAsync(ct);

    [HttpPost("templates"), HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> CreateTemplate(MessageTemplateRequest request, CancellationToken ct) =>
        Ok(new { id = await messaging.CreateTemplateAsync(request, ct) });

    [HttpPut("templates/{id:guid}"), HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> UpdateTemplate(Guid id, MessageTemplateRequest request, CancellationToken ct)
    {
        await messaging.UpdateTemplateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("templates/{id:guid}"), HasPermission(Permissions.TemplatesManage)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        await messaging.DeleteTemplateAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Meta WhatsApp Cloud API webhook: verification handshake + delivery status updates (signature-checked).</summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController(MessagingService messaging, IOptions<WhatsAppOptions> options, ILogger<WhatsAppWebhookController> logger)
    : ControllerBase
{
    [HttpGet]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = options.Value.WebhookVerifyToken;
        if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && token == expected) return Content(challenge ?? "");
        return Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);

        var secret = options.Value.AppSecret;
        if (string.IsNullOrEmpty(secret)) return NotFound(); // webhook not enabled
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expected))) return Unauthorized();

        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var entry in doc.RootElement.GetProperty("entry").EnumerateArray())
            foreach (var change in entry.GetProperty("changes").EnumerateArray())
            {
                if (!change.GetProperty("value").TryGetProperty("statuses", out var statuses)) continue;
                foreach (var s in statuses.EnumerateArray())
                {
                    var status = s.GetProperty("status").GetString() switch
                    {
                        "sent" => MessageStatus.Sent,
                        "delivered" => MessageStatus.Delivered,
                        "read" => MessageStatus.Read,
                        "failed" => MessageStatus.Failed,
                        _ => (MessageStatus?)null
                    };
                    if (status is null) continue;
                    string? error = s.TryGetProperty("errors", out var errs) ? errs[0].GetProperty("title").GetString() : null;
                    await messaging.ApplyProviderStatusAsync(s.GetProperty("id").GetString()!, status.Value, error, ct);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Unrecognised WhatsApp webhook payload");
        }
        return Ok();
    }
}

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<NotificationListDto> List([FromQuery] bool unreadOnly, [FromQuery] int take = 30, CancellationToken ct = default) =>
        notifications.ListAsync(unreadOnly, take, ct);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
