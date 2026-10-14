using Tahona.Domain.Entities;

namespace Tahona.Application.Common;

public record AccessToken(string Token, DateTime ExpiresAt);

public record GeneratedRefreshToken(string RawToken, string TokenHash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, Role role, Clinic clinic, Guid? dentistId);
    GeneratedRefreshToken CreateRefreshToken();
    string HashToken(string rawToken);
}

// ---------------------------------------------------------------- WhatsApp

/// <param name="TemplateParameters">Values of the template variables in the order they appear (for approved provider templates).</param>
public record WhatsAppSendRequest(string ToInternationalNumber, string Body, string? ProviderTemplateName, string Language,
    IReadOnlyList<string>? TemplateParameters = null);

public record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error, bool RequiresManualSend, string? ManualSendUrl);

/// <summary>
/// Abstraction over a WhatsApp Business provider. The default implementation produces a wa.me deep link
/// for manual sending; the Meta Cloud API implementation is activated by environment credentials.
/// </summary>
public interface IWhatsAppProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken ct);
}

/// <summary>The WhatsApp Business number a set of credentials belongs to, as Meta reports it.</summary>
public record WhatsAppNumberInfo(string PhoneNumberId, string? DisplayPhoneNumber, string? VerifiedName);

/// <summary>Checks WhatsApp Cloud API credentials with the provider before they are stored.</summary>
public interface IWhatsAppAccountVerifier
{
    /// <returns>The number's details, or an error message from the provider.</returns>
    Task<(WhatsAppNumberInfo? Number, string? Error)> VerifyAsync(string phoneNumberId, string accessToken, CancellationToken ct);
}

// ---------------------------------------------------------------- Secrets

/// <summary>Encrypts secrets a clinic entrusts to the system (provider tokens) before they are stored.</summary>
public interface ISecretProtector
{
    /// <summary>False when no encryption key is configured: secrets cannot be stored at all.</summary>
    bool IsAvailable { get; }
    string Protect(string secret);
    /// <returns>The secret, or null when it cannot be decrypted (wrong or rotated key).</returns>
    string? Unprotect(string protectedValue);
}

// ---------------------------------------------------------------- E-mail

public record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Outgoing e-mail for account links (invitations, password resets). Optional: without it an operator passes links on.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }
    /// <returns>False when the message could not be handed to the mail server.</returns>
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Facts about this deployment that staff see: where the app lives, how to pay, whom to contact.</summary>
public interface IPlatformInfo
{
    string PublicUrl { get; }
    string? SupportContact { get; }
    string? PaymentInstructions { get; }
}

// ---------------------------------------------------------------- AI

/// <summary>A read-only, tenant-scoped capability the model may call. Results are JSON produced from clinic data.</summary>
public record AiTool(
    string Name,
    string Description,
    System.Text.Json.JsonElement InputSchema,
    Func<System.Text.Json.JsonElement, CancellationToken, Task<string>> ExecuteAsync);

public record AiChatTurn(bool IsUser, string Text);

public record AiToolTrace(string Name, string InputJson, bool IsError);

public record AiRunResult(string Text, IReadOnlyList<AiToolTrace> ToolCalls, bool Refused);

/// <summary>LLM provider port. Implementations: Google Gemini (default) and Anthropic Claude, chosen by Ai:Provider.</summary>
public interface IAiProvider
{
    /// <summary>Display name of the backing service, e.g. "Gemini".</summary>
    string ProviderName { get; }
    bool IsConfigured { get; }
    string ModelName { get; }

    /// <summary>Runs an agentic tool loop: the model may call <paramref name="tools"/> until it produces a final answer.</summary>
    Task<AiRunResult> RunWithToolsAsync(string system, IReadOnlyList<AiChatTurn> conversation, IReadOnlyList<AiTool> tools,
        int maxIterations, CancellationToken ct);

    /// <summary>Single call constrained to a JSON schema (structured output). Returns null when the model declines.</summary>
    Task<string?> CompleteJsonAsync(string system, string userText, System.Text.Json.JsonElement schema, CancellationToken ct);
}

/// <summary>
/// How much patient-level data may leave the system for the AI provider.
/// Off: aggregate figures only. Minimized: patients appear as file numbers, without names or phone numbers.
/// Full: names and phone numbers are included (only with a reviewed data-processing agreement).
/// </summary>
public enum AiPatientDataMode { Off, Minimized, Full }

/// <summary>Deployment-level AI data policy (Ai:PatientData).</summary>
public interface IAiDataPolicy
{
    AiPatientDataMode PatientData { get; }
}

// ---------------------------------------------------------------- Sequences

public enum SequenceKind { Patient, TreatmentPlan, Invoice, Receipt }

/// <summary>Atomically allocates human-readable document numbers per clinic (P-00042, INV-1043...).</summary>
public interface ISequenceGenerator
{
    Task<string> NextAsync(SequenceKind kind, CancellationToken ct);
}

// ---------------------------------------------------------------- Search

/// <summary>Provider-specific text matching (Postgres: ILIKE backed by trigram GIN indexes).</summary>
public interface ISearchProvider
{
    IQueryable<Patient> FilterPatients(IQueryable<Patient> query, string term);
}

// ---------------------------------------------------------------- Files

public interface IFileStorage
{
    /// <summary>Where files are kept, for display ("Local disk", "S3 bucket …").</summary>
    string Name { get; }
    Task<string> SaveAsync(Guid clinicId, Guid patientId, string fileName, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
