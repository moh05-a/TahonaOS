using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Infrastructure.Ai;

namespace Tahona.Tests;

/// <summary>Drives the Gemini provider against a scripted HTTP handler and inspects the exact requests it sends.</summary>
public class GeminiAiProviderTests
{
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);
        public List<(Uri Uri, string? ApiKey, JsonObject Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            Requests.Add((request.RequestUri!, request.Headers.TryGetValues("x-goog-api-key", out var k) ? k.First() : null, body));
            var (status, json) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static GeminiAiProvider Create(ScriptedHandler handler, string? model = null) =>
        new(new HttpClient(handler), Options.Create(new AiOptions { ApiKey = "test-key", Model = model }), NullLogger<GeminiAiProvider>.Instance);

    private static AiTool Tool(string name, string schema, Func<JsonElement, string> run) =>
        new(name, $"{name} description", JsonDocument.Parse(schema).RootElement, (input, _) => Task.FromResult(run(input)));

    private const string FunctionCallTurn = """
        {"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[
          {"thought":true,"text":"I should look up balances."},
          {"functionCall":{"id":"call-1","name":"list_outstanding_balances","args":{"limit":3}},"thoughtSignature":"SIG-abc"}
        ]}}]}
        """;

    private const string FinalTurn = """
        {"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[{"text":"Sara owes **200 JOD**."}]}}]}
        """;

    [Fact]
    public async Task Tool_loop_executes_calls_and_echoes_the_model_turn_with_its_thought_signature()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, FunctionCallTurn), (HttpStatusCode.OK, FinalTurn));
        JsonElement? received = null;
        var tools = new[]
        {
            Tool("list_outstanding_balances", """{"type":"object","properties":{"limit":{"type":"integer","minimum":1}},"required":[]}""",
                input => { received = input; return """{"patients":[{"patient":"Sara","balance":200}]}"""; }),
            Tool("get_recovery_summary", """{"type":"object","properties":{},"required":[]}""", _ => "{}")
        };

        var result = await Create(handler).RunWithToolsAsync("system rules", [new AiChatTurn(true, "Who owes us the most?")], tools, 5, CancellationToken.None);

        Assert.Equal("Sara owes **200 JOD**.", result.Text);
        Assert.False(result.Refused);
        Assert.Equal(3, received!.Value.GetProperty("limit").GetInt32());
        var trace = Assert.Single(result.ToolCalls);
        Assert.Equal("list_outstanding_balances", trace.Name);

        // First request: endpoint, key header, system instruction, sanitised tool declarations.
        var first = handler.Requests[0];
        Assert.EndsWith("/v1beta/models/gemini-3.8-flash:generateContent", first.Uri.AbsolutePath);
        Assert.Equal("test-key", first.ApiKey);
        Assert.Equal("system rules", first.Body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        var declarations = first.Body["tools"]![0]!["functionDeclarations"]!.AsArray();
        Assert.Null(declarations[0]!["parameters"]!["required"]);           // empty "required" removed
        Assert.Null(declarations[1]!.AsObject()["parameters"]);             // no-argument tool omits parameters

        // Second request: model turn replayed verbatim (signature + id kept), followed by one functionResponse turn.
        var contents = handler.Requests[1].Body["contents"]!.AsArray();
        Assert.Equal(3, contents.Count);
        var modelParts = contents[1]!["parts"]!.AsArray();
        Assert.Equal("model", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("SIG-abc", modelParts[1]!["thoughtSignature"]!.GetValue<string>());
        var response = contents[2]!["parts"]![0]!["functionResponse"]!;
        Assert.Equal("user", contents[2]!["role"]!.GetValue<string>());
        Assert.Equal("call-1", response["id"]!.GetValue<string>());
        Assert.Equal("list_outstanding_balances", response["name"]!.GetValue<string>());
        Assert.Equal(200, response["response"]!["patients"]![0]!["balance"]!.GetValue<int>());
    }

    [Fact]
    public async Task Thought_parts_are_not_shown_to_the_user()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK,
            """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"thought":true,"text":"hidden reasoning"},{"text":"Visible answer"}]}}]}"""));
        var result = await Create(handler).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None);
        Assert.Equal("Visible answer", result.Text);
    }

    [Theory]
    [InlineData("""{"promptFeedback":{"blockReason":"SAFETY"}}""")]
    [InlineData("""{"candidates":[{"finishReason":"PROHIBITED_CONTENT"}]}""")]
    public async Task Blocked_responses_are_reported_as_refusals(string body)
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, body));
        var result = await Create(handler).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None);
        Assert.True(result.Refused);
    }

    [Fact]
    public async Task Unknown_tool_and_tool_errors_are_returned_to_the_model_not_thrown()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"does_not_exist","args":{}}}]}}]}"""),
            (HttpStatusCode.OK, FinalTurn));
        var result = await Create(handler).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None);

        Assert.True(Assert.Single(result.ToolCalls).IsError);
        var error = handler.Requests[1].Body["contents"]![2]!["parts"]![0]!["functionResponse"]!["response"]!["error"]!.GetValue<string>();
        Assert.Contains("does_not_exist", error);
    }

    [Fact]
    public async Task Json_completion_requests_structured_output_with_a_gemini_compatible_schema()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK,
            """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"chief_complaint\":\"pain\"}"}]}}]}"""));
        var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"chief_complaint":{"type":"string"}},"required":["chief_complaint"],"additionalProperties":false}""").RootElement;

        var json = await Create(handler).CompleteJsonAsync("organise only", "note text", schema, CancellationToken.None);

        Assert.Equal("""{"chief_complaint":"pain"}""", json);
        var config = handler.Requests[0].Body["generationConfig"]!;
        Assert.Equal("application/json", config["responseMimeType"]!.GetValue<string>());
        Assert.Null(config["responseSchema"]!.AsObject()["additionalProperties"]);   // not accepted by Gemini
        Assert.Equal("chief_complaint", config["responseSchema"]!["required"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "limit")]
    [InlineData(HttpStatusCode.Forbidden, "API key")]
    [InlineData(HttpStatusCode.InternalServerError, "temporarily unavailable")]
    public async Task Provider_errors_become_friendly_messages_without_leaking_details(HttpStatusCode status, string expected)
    {
        var handler = new ScriptedHandler((status, """{"error":{"message":"internal detail with key=SECRET"}}"""));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(handler).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
    }

    [Fact]
    public async Task Invalid_key_reported_as_400_is_explained_as_a_key_problem()
    {
        // Shape returned by the live API for a bad key.
        var handler = new ScriptedHandler((HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}"""));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(handler).CompleteJsonAsync("s", "text", JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""").RootElement, CancellationToken.None));
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Overloaded_model_is_retried_before_giving_up()
    {
        // Shape returned by the live API when the free tier is busy.
        const string overloaded = """{"error":{"code":503,"message":"This model is currently experiencing high demand.","status":"UNAVAILABLE"}}""";

        var recovers = new ScriptedHandler((HttpStatusCode.ServiceUnavailable, overloaded), (HttpStatusCode.OK, FinalTurn));
        var result = await Create(recovers).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None);
        Assert.Equal("Sara owes **200 JOD**.", result.Text);
        Assert.Equal(2, recovers.Requests.Count);

        var staysDown = new ScriptedHandler((HttpStatusCode.ServiceUnavailable, overloaded), (HttpStatusCode.ServiceUnavailable, overloaded),
            (HttpStatusCode.ServiceUnavailable, overloaded));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Create(staysDown).RunWithToolsAsync("s", [new AiChatTurn(true, "hi")], [], 3, CancellationToken.None));
        Assert.Contains("temporarily unavailable", ex.Message);
        Assert.Equal(3, staysDown.Requests.Count);
    }

    [Fact]
    public void Model_from_another_provider_falls_back_to_the_gemini_default()
    {
        Assert.Equal("gemini-3.8-flash", Create(new ScriptedHandler(), "claude-opus-5-5").ModelName);
        Assert.Equal("gemini-3.5-flash-lite", Create(new ScriptedHandler(), "gemini-3.5-flash-lite").ModelName);
    }
}
