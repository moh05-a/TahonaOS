using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Ai;

/// <summary>
/// Claude Messages API via the official Anthropic C# SDK. The tool loop is append-only (assistant turns,
/// including thinking blocks, are echoed back unchanged) and policy refusals use the server-side "default" fallback.
/// </summary>
public sealed class ClaudeAiProvider : IAiProvider
{
    /// <summary>Server-side refusal fallback: a declined request is re-served by a fallback model chosen per refusal category.</summary>
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AiOptions _options;
    private readonly ILogger<ClaudeAiProvider> _logger;
    private readonly AnthropicClient? _client;

    public ClaudeAiProvider(IOptions<AiOptions> options, ILogger<ClaudeAiProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        var key = string.IsNullOrWhiteSpace(_options.ApiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : _options.ApiKey;
        if (!string.IsNullOrWhiteSpace(key)) _client = new AnthropicClient { ApiKey = key };
    }

    public const string DefaultModel = "claude-opus-5-5";

    public string ProviderName => "Claude";
    public bool IsConfigured => _client is not null;

    /// <summary>Ignores a model id left over from another provider (e.g. AI__MODEL=gemini-…).</summary>
    public string ModelName =>
        _options.Model is { } m && m.Trim().StartsWith("claude", StringComparison.OrdinalIgnoreCase) ? m.Trim() : DefaultModel;

    public async Task<AiRunResult> RunWithToolsAsync(string system, IReadOnlyList<AiChatTurn> conversation, IReadOnlyList<AiTool> tools,
        int maxIterations, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("AI provider is not configured.");
        var toolDefs = tools.Select(ToBetaTool).ToList();
        var byName = tools.ToDictionary(t => t.Name);
        var traces = new List<AiToolTrace>();

        List<BetaMessageParam> messages = conversation
            .Select(t => new BetaMessageParam { Role = t.IsUser ? Role.User : Role.Assistant, Content = t.Text })
            .ToList();

        for (var i = 0; i < maxIterations; i++)
        {
            BetaMessage response;
            try
            {
                response = await client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = ModelName,
                    MaxTokens = _options.MaxTokens,
                    System = system,
                    OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                    Betas = [FallbackBeta],
                    Fallbacks = new Default(),
                    Tools = toolDefs,
                    Messages = messages
                }, cancellationToken: ct);
            }
            catch (AnthropicApiException ex)
            {
                _logger.LogError(ex, "Claude API call failed");
                throw new BusinessRuleException("The AI service is temporarily unavailable. Please try again.");
            }

            if (response.StopReason == "refusal")
                return new AiRunResult("I can't help with that request.", traces, true);

            List<BetaContentBlockParam> assistant = [];
            List<BetaContentBlockParam> results = [];
            var text = new List<string>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t))
                {
                    assistant.Add(new BetaTextBlockParam { Text = t.Text });
                    text.Add(t.Text);
                }
                else if (block.TryPickThinking(out var th))
                {
                    assistant.Add(new BetaThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                }
                else if (block.TryPickRedactedThinking(out var rt))
                {
                    assistant.Add(new BetaRedactedThinkingBlockParam { Data = rt.Data });
                }
                else if (block.TryPickToolUse(out var tu))
                {
                    assistant.Add(new BetaToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                    var inputJson = JsonSerializer.Serialize(tu.Input);
                    string content;
                    var isError = false;
                    if (!byName.TryGetValue(tu.Name, out var tool))
                    {
                        content = $"Unknown tool '{tu.Name}'.";
                        isError = true;
                    }
                    else
                    {
                        try
                        {
                            content = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(tu.Input), ct);
                        }
                        catch (AppException ex)
                        {
                            content = ex.Message;
                            isError = true;
                        }
                    }
                    traces.Add(new AiToolTrace(tu.Name, inputJson, isError));
                    results.Add(new BetaToolResultBlockParam { ToolUseID = tu.ID, Content = content, IsError = isError });
                }
            }

            if (response.StopReason != "tool_use" || results.Count == 0)
            {
                var answer = string.Join("\n\n", text).Trim();
                if (response.StopReason == "max_tokens") answer += "\n\n_(Answer truncated.)_";
                return new AiRunResult(answer, traces, false);
            }

            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = assistant });
            messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }

        return new AiRunResult("I could not complete this analysis within the allowed number of steps. Try a more specific question.", traces, false);
    }

    public async Task<string?> CompleteJsonAsync(string system, string userText, JsonElement schema, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("AI provider is not configured.");
        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelName,
                MaxTokens = 4000,
                System = system,
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Low,
                    Format = new BetaJsonOutputFormat
                    {
                        Schema = schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
                    }
                },
                Betas = [FallbackBeta],
                Fallbacks = new Default(),
                Messages = [new BetaMessageParam { Role = Role.User, Content = userText }]
            }, cancellationToken: ct);
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogError(ex, "Claude API call failed");
            throw new BusinessRuleException("The AI service is temporarily unavailable. Please try again.");
        }

        if (response.StopReason == "refusal") return null;
        var json = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    private static BetaToolUnion ToBetaTool(AiTool tool)
    {
        var properties = tool.InputSchema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : new Dictionary<string, JsonElement>();
        var required = tool.InputSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()!).ToList()
            : [];

        return new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required }
        };
    }
}
