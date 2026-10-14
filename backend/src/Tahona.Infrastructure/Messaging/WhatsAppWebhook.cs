using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tahona.Domain.Enums;

namespace Tahona.Infrastructure.Messaging;

/// <summary>Signature check and payload parsing for the Meta WhatsApp Cloud API webhook. Pure functions, no I/O.</summary>
public static class WhatsAppWebhook
{
    public abstract record Event(string MessageId);

    public sealed record StatusEvent(string MessageId, MessageStatus Status, string? Error) : Event(MessageId);

/// <param name="PhoneNumberId">The business number that received the message; identifies the clinic.</param>
    public sealed record InboundEvent(string MessageId, string From, string Text, DateTime ReceivedAt, string? PhoneNumberId) : Event(MessageId);

    /// <summary>Validates X-Hub-Signature-256 ("sha256=" + hex HMAC of the raw body with the app secret) in constant time.</summary>
    public static bool IsSignatureValid(ReadOnlySpan<byte> body, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(appSecret)) return false;
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signatureHeader.Trim().ToLowerInvariant()), Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Extracts delivery statuses and inbound messages. Unknown shapes are skipped, never guessed.</summary>
    public static IReadOnlyList<Event> Parse(ReadOnlySpan<byte> body)
    {
        var events = new List<Event>();
        using var doc = JsonDocument.Parse(body.ToArray());
        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return events;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;

                if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                    foreach (var s in statuses.EnumerateArray())
                        if (ParseStatus(s) is { } status) events.Add(status);

                var receivingNumber = value.TryGetProperty("metadata", out var metadata) ? Str(metadata, "phone_number_id") : null;
                if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                    foreach (var m in messages.EnumerateArray())
                        if (ParseInbound(m, receivingNumber) is { } inbound) events.Add(inbound);
            }
        }
        return events;
    }

    private static StatusEvent? ParseStatus(JsonElement s)
    {
        var id = Str(s, "id");
        MessageStatus? status = Str(s, "status") switch
        {
            "sent" => MessageStatus.Sent,
            "delivered" => MessageStatus.Delivered,
            "read" => MessageStatus.Read,
            "failed" => MessageStatus.Failed,
            _ => null
        };
        if (id is null || status is null) return null;

        string? error = null;
        if (s.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            error = Str(errors[0], "title") ?? Str(errors[0], "message");
        return new StatusEvent(id, status.Value, error);
    }

    private static InboundEvent? ParseInbound(JsonElement m, string? phoneNumberId)
    {
        var id = Str(m, "id");
        var from = Str(m, "from");
        if (id is null || from is null) return null;

        var type = Str(m, "type") ?? "unknown";
        var text = type switch
        {
            "text" => m.TryGetProperty("text", out var t) ? Str(t, "body") : null,
            "button" => m.TryGetProperty("button", out var b) ? Str(b, "text") : null,
            "interactive" => m.TryGetProperty("interactive", out var i)
                ? (i.TryGetProperty("button_reply", out var br) ? Str(br, "title") : i.TryGetProperty("list_reply", out var lr) ? Str(lr, "title") : null)
                : null,
            _ => null
        };
        // Media and other types are recorded as a placeholder; the content itself is not downloaded.
        text ??= $"[{type}]";

        var receivedAt = long.TryParse(Str(m, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
            : DateTime.UtcNow;
        return new InboundEvent(id, from, text, receivedAt, phoneNumberId);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
