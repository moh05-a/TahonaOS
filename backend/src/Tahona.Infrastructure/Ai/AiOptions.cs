namespace Tahona.Infrastructure.Ai;

public class AiOptions
{
    public const string Section = "Ai";
    public const string Gemini = "gemini";
    public const string Claude = "claude";

    /// <summary>Which LLM backs the assistant and note structuring: "gemini" (default) or "claude". Env var AI__PROVIDER.</summary>
    public string Provider { get; set; } = Gemini;

    /// <summary>API key for the selected provider. Read from env var AI__APIKEY. Never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model id. Empty = the provider's default (gemini-3.8-flash / claude-opus-5-5).</summary>
    public string? Model { get; set; }

    public int MaxTokens { get; set; } = 16000;

    /// <summary>
    /// What patient-level data the provider may receive: "off", "minimized" (default: file numbers instead of names
    /// and phone numbers) or "full". Use "full" only under a data-processing agreement that covers patient data.
    /// </summary>
    public string PatientData { get; set; } = "minimized";

    public bool UseClaude => string.Equals(Provider?.Trim(), Claude, StringComparison.OrdinalIgnoreCase);
}
