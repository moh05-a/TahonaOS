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

/// <summary>
/// Meta WhatsApp Cloud API webhook: the verification handshake, delivery statuses and inbound patient messages.
/// Every POST must carry a valid X-Hub-Signature-256 over the raw request body. Processing is idempotent, so Meta
/// re-delivering an event changes nothing.
/// </summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController(MessagingService messaging, IOptions<WhatsAppOptions> options, ILogger<WhatsAppWebhookController> logger)
    : ControllerBase
{
    private const int MaxBodyBytes = 1024 * 1024;

    [HttpGet]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = options.Value.WebhookVerifyToken;
        if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && token is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            return Content(challenge ?? "");
        return Forbid();
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var secret = options.Value.AppSecret;
        if (string.IsNullOrEmpty(secret)) return NotFound(); // webhook not enabled

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (!WhatsAppWebhook.IsSignatureValid(body, Request.Headers["X-Hub-Signature-256"].ToString(), secret)) return Unauthorized();

        try
        {
            foreach (var e in WhatsAppWebhook.Parse(body))
            {
                switch (e)
                {
                    case WhatsAppWebhook.StatusEvent status:
                        await messaging.ApplyProviderStatusAsync(status.MessageId, status.Status, status.Error, ct);
                        break;
                    case WhatsAppWebhook.InboundEvent inbound:
                        var result = await messaging.ReceiveInboundAsync(inbound.MessageId, inbound.From, inbound.Text, inbound.ReceivedAt, ct, inbound.PhoneNumberId);
                        if (result == InboundResult.Unmatched)
                            logger.LogInformation("Inbound WhatsApp message {MessageId} did not match a recent conversation; ignored.", inbound.MessageId);
                        break;
                }
            }
        }
        catch (JsonException ex)
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
