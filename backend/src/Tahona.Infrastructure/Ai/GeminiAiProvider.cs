using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;

namespace Tahona.Infrastructure.Ai;

/// <summary>
/// Google Gemini via the generateContent REST API (works with a free-tier Google AI Studio key).
/// The tool loop is append-only: each model turn is sent back exactly as received, which preserves the
/// thought signatures and call ids Gemini's thinking models require for function calling.
/// </summary>
public sealed class GeminiAiProvider : IAiProvider
{
    public const string DefaultModel = "gemini-3.8-flash";
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    /// <summary>Schema keywords Gemini's function and response schemas accept; anything else is dropped.</summary>
    private static readonly HashSet<string> SchemaKeywords =
        ["type", "description", "enum", "properties", "required", "items", "minimum", "maximum", "nullable", "format"];

    private static readonly HashSet<string> RefusalReasons =
        ["SAFETY", "PROHIBITED_CONTENT", "BLOCKLIST", "SPII", "RECITATION", "IMAGE_SAFETY"];

    private static readonly TimeSpan[] OverloadRetryDelays = [TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(2)];

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<GeminiAiProvider> _logger;
    private readonly string? _apiKey;

    public GeminiAiProvider(HttpClient http, IOptions<AiOptions> options, ILogger<GeminiAiProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _apiKey = FirstNonEmpty(_options.ApiKey, Environment.GetEnvironmentVariable("GEMINI_API_KEY"), Environment.GetEnvironmentVariable("GOOGLE_API_KEY"));
    }

    public string ProviderName => "Gemini";
    public bool IsConfigured => _apiKey is not null;

    /// <summary>Ignores a model id left over from another provider (e.g. AI__MODEL=claude-…).</summary>
    public string ModelName =>
        _options.Model is { } m && m.Trim().StartsWith("gem", StringComparison.OrdinalIgnoreCase) ? m.Trim() : DefaultModel;

    public async Task<AiRunResult> RunWithToolsAsync(string system, IReadOnlyList<AiChatTurn> conversation, IReadOnlyList<AiTool> tools,
        int maxIterations, CancellationToken ct)
    {
        var byName = tools.ToDictionary(t => t.Name);
        var traces = new List<AiToolTrace>();
        var contents = new JsonArray();
        foreach (var turn in conversation) contents.Add(TextTurn(turn.IsUser ? "user" : "model", turn.Text));

        for (var i = 0; i < maxIterations; i++)
        {
            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
                ["contents"] = contents.DeepClone(),
                ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = _options.MaxTokens }
            };
            if (tools.Count > 0)
                body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = new JsonArray(tools.Select(ToDeclaration).ToArray<JsonNode?>()) });

            var response = await SendAsync(body, ct);
            if (IsRefusal(response, out var candidate))
                return new AiRunResult("I can't help with that request.", traces, true);

            var modelContent = candidate?["content"] as JsonObject;
            var parts = modelContent?["parts"] as JsonArray ?? [];
            var text = new List<string>();
            var functionResponses = new JsonArray();

            foreach (var part in parts.OfType<JsonObject>())
            {
                if (part["functionCall"] is JsonObject call)
                {
                    var name = call["name"]?.GetValue<string>() ?? "";
                    var args = call["args"] ?? new JsonObject();
                    var result = await ExecuteToolAsync(byName, name, args, traces, ct);
                    var functionResponse = new JsonObject { ["name"] = name, ["response"] = result };
                    if (call["id"] is { } id) functionResponse["id"] = id.DeepClone();
                    functionResponses.Add(new JsonObject { ["functionResponse"] = functionResponse });
                }
                else if (part["text"] is { } t && part["thought"]?.GetValue<bool>() != true)
                {
                    text.Add(t.GetValue<string>());
                }
            }

            var finish = candidate?["finishReason"]?.GetValue<string>();
            if (functionResponses.Count == 0)
            {
                var answer = string.Join("", text).Trim();
                if (answer.Length == 0 && finish is "MALFORMED_FUNCTION_CALL" or "UNEXPECTED_TOOL_CALL")
                    answer = "I could not complete this analysis. Try rephrasing the question.";
                if (finish == "MAX_TOKENS") answer += "\n\n_(Answer truncated.)_";
                return new AiRunResult(answer, traces, false);
            }

            // Echo the model turn verbatim (keeps thoughtSignature / call ids), then answer every call in one user turn.
            var echoed = (JsonObject)modelContent!.DeepClone();
            echoed["role"] = "model";
            contents.Add(echoed);
            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = functionResponses });
        }

        return new AiRunResult("I could not complete this analysis within the allowed number of steps. Try a more specific question.", traces, false);
    }

    public async Task<string?> CompleteJsonAsync(string system, string userText, JsonElement schema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
            ["contents"] = new JsonArray(TextTurn("user", userText)),
            ["generationConfig"] = new JsonObject
            {
                // Thinking tokens count against this limit, so leave generous headroom for a small JSON object.
                ["maxOutputTokens"] = 8192,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = SanitizeSchema(JsonNode.Parse(schema.GetRawText())!)
            }
        };

        var response = await SendAsync(body, ct);
        if (IsRefusal(response, out var candidate)) return null;

        var parts = candidate?["content"]?["parts"] as JsonArray ?? [];
        var json = string.Concat(parts.OfType<JsonObject>()
            .Where(p => p["text"] is not null && p["thought"]?.GetValue<bool>() != true)
            .Select(p => p["text"]!.GetValue<string>()));
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    // ------------------------------------------------------------------ HTTP

    private async Task<JsonObject> SendAsync(JsonObject body, CancellationToken ct)
    {
        if (_apiKey is null) throw new InvalidOperationException("AI provider is not configured.");

        var json = body.ToJsonString();
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{Uri.EscapeDataString(ModelName)}:generateContent")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _apiKey);

            HttpResponseMessage response;
            string payload;
            try
            {
                response = await _http.SendAsync(request, ct);
                payload = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                _logger.LogError(ex, "Gemini API call failed");
                throw new BusinessRuleException("The AI service is temporarily unavailable. Please try again.");
            }

            using var _ = response;

            // 503 UNAVAILABLE means the model is briefly overloaded (common on the free tier): wait and retry.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < OverloadRetryDelays.Length)
            {
                _logger.LogWarning("Gemini API overloaded (503); retry {Attempt} of {Max}", attempt + 1, OverloadRetryDelays.Length);
                await Task.Delay(OverloadRetryDelays[attempt], ct);
                continue;
            }

            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(payload) as JsonObject ?? throw new BusinessRuleException("The AI service returned an unexpected response.");

            // Never surface the provider's raw error to end users; it can echo request details.
            _logger.LogError("Gemini API returned {Status}: {Body}", (int)response.StatusCode, Truncate(payload, 800));
            // Gemini reports a bad key as 400 INVALID_ARGUMENT with reason API_KEY_INVALID (not 401/403).
            var invalidKey = payload.Contains("API_KEY_INVALID", StringComparison.Ordinal);
            throw response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    new BusinessRuleException("The AI usage limit has been reached for now (free-tier quota). Please try again in a minute."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new BusinessRuleException("The AI service rejected the API key. Ask an administrator to check AI__APIKEY."),
                HttpStatusCode.BadRequest when invalidKey =>
                    new BusinessRuleException("The AI service rejected the API key. Ask an administrator to check AI__APIKEY."),
                HttpStatusCode.NotFound =>
                    new BusinessRuleException($"The AI model '{ModelName}' is not available for this API key. Ask an administrator to check AI__MODEL."),
                _ => new BusinessRuleException("The AI service is temporarily unavailable. Please try again.")
            };
        }
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsRefusal(JsonObject response, out JsonObject? candidate)
    {
        candidate = (response["candidates"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        if (response["promptFeedback"]?["blockReason"] is not null) return true;
        if (candidate is null) return true;
        return candidate["finishReason"]?.GetValue<string>() is { } reason && RefusalReasons.Contains(reason);
    }

    private static async Task<JsonObject> ExecuteToolAsync(Dictionary<string, AiTool> tools, string name, JsonNode args,
        List<AiToolTrace> traces, CancellationToken ct)
    {
        var inputJson = args.ToJsonString();
        if (!tools.TryGetValue(name, out var tool))
        {
            traces.Add(new AiToolTrace(name, inputJson, true));
            return new JsonObject { ["error"] = $"Unknown tool '{name}'." };
        }

        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var output = await tool.ExecuteAsync(doc.RootElement.Clone(), ct);
            traces.Add(new AiToolTrace(name, inputJson, false));
            // functionResponse.response must be a JSON object.
            return TryParse(output) as JsonObject ?? new JsonObject { ["result"] = TryParse(output) ?? output };
        }
        catch (AppException ex)
        {
            traces.Add(new AiToolTrace(name, inputJson, true));
            return new JsonObject { ["error"] = ex.Message };
        }
    }

    private static JsonObject ToDeclaration(AiTool tool)
    {
        var declaration = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description };
        var schema = SanitizeSchema(JsonNode.Parse(tool.InputSchema.GetRawText())!);
        // Gemini rejects an object schema with no properties: a tool without arguments simply omits "parameters".
        if (schema is JsonObject o && o["properties"] is JsonObject { Count: > 0 }) declaration["parameters"] = schema;
        return declaration;
    }

    /// <summary>Reduces a JSON Schema to the subset Gemini accepts (drops additionalProperties, $schema, empty "required"…).</summary>
    internal static JsonNode SanitizeSchema(JsonNode node)
    {
        if (node is not JsonObject source) return node.DeepClone();

        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            if (value is null || !SchemaKeywords.Contains(key)) continue;
            switch (key)
            {
                case "properties" when value is JsonObject props:
                    var cleaned = new JsonObject();
                    foreach (var (name, prop) in props)
                        if (prop is not null) cleaned[name] = SanitizeSchema(prop);
                    result[key] = cleaned;
                    break;
                case "items":
                    result[key] = SanitizeSchema(value);
                    break;
                case "required" when value is JsonArray { Count: 0 }:
                    break;
                default:
                    result[key] = value.DeepClone();
                    break;
            }
        }
        return result;
    }

    private static JsonObject TextTurn(string role, string text) =>
        new() { ["role"] = role, ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) };

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
