using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Services;

// ------------------------------------------------------------------ deployment facts

public class AppOptions
{
    public const string Section = "App";
    /// <summary>Address of the web app, used in invitation and password-reset links (env APP__PUBLICURL).</summary>
    public string PublicUrl { get; set; } = "http://localhost:5173";
    /// <summary>How clinics reach support, shown on the subscription page (env APP__SUPPORTCONTACT).</summary>
    public string? SupportContact { get; set; }
    /// <summary>How to pay, shown on the subscription page (env APP__PAYMENTINSTRUCTIONS).</summary>
    public string? PaymentInstructions { get; set; }
}

public sealed class PlatformInfo(IOptions<AppOptions> options) : IPlatformInfo
{
    public string PublicUrl => options.Value.PublicUrl.TrimEnd('/');
    public string? SupportContact => Clean(options.Value.SupportContact);
    public string? PaymentInstructions => Clean(options.Value.PaymentInstructions);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// ------------------------------------------------------------------ secrets

public class SecretsOptions
{
    public const string Section = "Secrets";
    /// <summary>
    /// Base64 of 32 random bytes (env SECRETS__ENCRYPTIONKEY, e.g. `openssl rand -base64 32`). Encrypts provider tokens
    /// that clinics store. Losing or changing it makes stored tokens unreadable; clinics then reconnect.
    /// </summary>
    public string? EncryptionKey { get; set; }
}

/// <summary>AES-256-GCM with a random nonce per value. Output: "v1." + base64(nonce | tag | ciphertext).</summary>
public sealed class AesSecretProtector : ISecretProtector
{
    private const string Prefix = "v1.";
    private readonly byte[]? _key;

    public AesSecretProtector(IOptions<SecretsOptions> options)
    {
        var configured = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(configured)) return;
        try
        {
            var key = Convert.FromBase64String(configured.Trim());
            if (key.Length == 32) _key = key;
        }
        catch (FormatException)
        {
            // Treated as not configured; IsAvailable reports it.
        }
    }

    public bool IsAvailable => _key is not null;

    public string Protect(string secret)
    {
        if (_key is null) throw new InvalidOperationException("Secrets:EncryptionKey is not configured.");
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var plain = Encoding.UTF8.GetBytes(secret);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string protectedValue)
    {
        if (_key is null || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            var data = Convert.FromBase64String(protectedValue[Prefix.Length..]);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (data.Length < nonceSize + tagSize) return null;
            var plain = new byte[data.Length - nonceSize - tagSize];
            using var aes = new AesGcm(_key, tagSize);
            aes.Decrypt(data.AsSpan(0, nonceSize), data.AsSpan(nonceSize + tagSize), data.AsSpan(nonceSize, tagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

// ------------------------------------------------------------------ e-mail

public class EmailOptions
{
    public const string Section = "Email";
    /// <summary>SMTP server (env EMAIL__HOST). Without it no e-mail is sent and links are passed on by an operator.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Sender address, e.g. "TahonaOS &lt;no-reply@example.com&gt;" (env EMAIL__FROM).</summary>
    public string? From { get; set; }
    /// <summary>"starttls" (default), "ssl" (implicit TLS, usually port 465) or "none" (local test servers only).</summary>
    public string Security { get; set; } = "starttls";
}

/// <summary>Sends plain-text account e-mail through an SMTP server. Failures are logged and reported, never thrown.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host) && !string.IsNullOrWhiteSpace(_options.From);

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        try
        {
            var mime = new MimeMessage();
            mime.From.Add(MailboxAddress.Parse(_options.From));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.TextBody };

            using var client = new SmtpClient { Timeout = 20_000 };
            var security = _options.Security.Trim().ToLowerInvariant() switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "none" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls
            };
            await client.ConnectAsync(_options.Host, _options.Port, security, ct);
            if (!string.IsNullOrWhiteSpace(_options.Username)) await client.AuthenticateAsync(_options.Username, _options.Password ?? "", ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The recipient address is not logged: it is personal data.
            logger.LogError(ex, "E-mail could not be sent ({Subject})", message.Subject);
            return false;
        }
    }
}
