using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class OpenAIAnalystTests
{
    internal static OpenAIAnalystOptions Options() => new()
    {
        Enabled = true, ApiKey = "test-api-key", Model = "gpt-6.1-sol", ReasoningEffort = "medium"
    };
    internal static MarketContext Context(bool leveraged = true) => new(
        DemoData.Products.First() with { IsLeveraged = leveraged },
        new("00631L", 40, 39, 41, 38, 39, 1000, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        [new(new DateOnly(2026, 10, 1), 39, 41, 38, 40, 1000)],
        new(1, 1, 6, 33.74m, DateTime.UtcNow), null);
    internal static string Response(string status = "completed", string? result = null, bool refusal = false) => JsonSerializer.Serialize(new
    {
        status, model = "gpt-6.1-sol",
        usage = new { input_tokens = 1234, output_tokens = 567, output_tokens_details = new { reasoning_tokens = 400 } },
        output = new object[]
        {
            new { type = "reasoning", summary = Array.Empty<string>() },
            new { type = "message", status = "completed", content = refusal
                ? new object[] { new { type = "refusal", refusal = "Declined" } }
                : new object[] { new { type = "output_text", text = result ?? JsonSerializer.Serialize(DemoData.MockResult("HOLD", true), new JsonSerializerOptions(JsonSerializerDefaults.Web)) } } }
        }
    });

    [Theory]
    [InlineData("gpt-6.1-sol", "medium", true)]
    [InlineData("configured-model", "high", false)]
    public async Task Request_UsesConfiguredModelReasoningAndSchema_AndPreservesUsage(string model, string effort, bool leveraged)
    {
        var options = Options(); options.Model = model; options.ReasoningEffort = effort;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(options.ApiKey, request.Headers.Authorization.Parameter);
            var body = await request.Content!.ReadAsStringAsync(ct);
            Assert.DoesNotContain(options.ApiKey, body);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal(model, root.GetProperty("model").GetString());
            Assert.Equal(effort, root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.TryGetProperty("temperature", out _));
            Assert.Equal(options.MaxOutputTokens, root.GetProperty("max_output_tokens").GetInt32());
            var format = root.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            var schema = format.GetProperty("schema");
            var fields = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order().ToArray();
            var expected = JsonSerializer.SerializeToElement(DemoData.MockResult("HOLD", true), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                .EnumerateObject().Select(x => x.Name).Order().ToArray();
            Assert.Equal(expected, fields);
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            var instructions = root.GetProperty("instructions").GetString()!;
            Assert.Equal(leveraged, instructions.Contains("daily rebalancing"));
            using var input = JsonDocument.Parse(root.GetProperty("input").GetString()!);
            Assert.Equal(6, input.RootElement.GetProperty("position").GetProperty("quantity").GetDecimal());
            Assert.False(input.RootElement.GetProperty("position").TryGetProperty("userId", out _));
            Assert.Single(input.RootElement.GetProperty("history").EnumerateArray());
            return Json(Response());
        }));
        var answer = await new OpenAIAnalyst(http, options).AnalyzeAsync(Context(leveraged), default);
        Assert.Equal(new TokenUsage(1234, 567), answer.Usage);
        Assert.Equal("gpt-6.1-sol", answer.Model);
        Assert.Equal(effort, answer.ReasoningEffort);
        Assert.Contains("marketTime", answer.Instructions!);
        Assert.Contains("reasoning_tokens", answer.RawResponse);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpFailure_IsNotMockSuccess_AndDoesNotLeakUpstreamBody(int status)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent("secret-upstream-body") })));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), default));
        Assert.Contains(status.ToString(), error.Message);
        Assert.DoesNotContain("secret-upstream-body", error.ToString());
        Assert.Null(error.Usage);
    }

    [Theory]
    [InlineData("incomplete", false)]
    [InlineData("completed", true)]
    public async Task IncompleteOrRefused_KeepsReportedUsage(string status, bool refusal)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Response(status, refusal: refusal)))));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), default));
        Assert.Equal(new TokenUsage(1234, 567), error.Usage);
    }

    [Theory]
    [InlineData("missing-confidence")]
    [InlineData("invalid-action")]
    [InlineData("invalid-enum")]
    [InlineData("missing-invalidation")]
    [InlineData("oversized-reduction")]
    [InlineData("extra-field")]
    [InlineData("invalid-json")]
    public async Task InvalidAnalysis_IsRejected(string scenario)
    {
        var node = JsonSerializer.SerializeToNode(DemoData.MockResult("HOLD", true), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        switch (scenario)
        {
            case "missing-confidence": node.AsObject().Remove("confidence"); break;
            case "invalid-action": node["action"] = "BUY"; break;
            case "invalid-enum": node["trend"] = "invented"; break;
            case "missing-invalidation": node["invalidation"] = ""; break;
            case "oversized-reduction": node["action"] = "REDUCE"; node["quantity"] = 7; break;
            case "extra-field": node["unexpected"] = true; break;
        }
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Response(result: scenario == "invalid-json" ? "not-json" : node.ToJsonString())))));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), default));
        Assert.Equal(new TokenUsage(1234, 567), error.Usage);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var http = new HttpClient(new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(Response()); }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), cancellation.Token));
    }

    [Fact]
    public async Task CacheUsageIsReadAndValidated()
    {
        var node = JsonNode.Parse(Response())!;
        node["usage"]!["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 128 };
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(node.ToJsonString()))));
        var answer = await new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), default);
        Assert.Equal(new TokenUsage(1234, 567, 128), answer.Usage);
        node["usage"]!["input_tokens_details"]!["cached_tokens"] = -1;
        await Assert.ThrowsAsync<AIProviderException>(() => new OpenAIAnalyst(http, Options()).AnalyzeAsync(Context(), default));
    }

    [Fact]
    public void EnabledConfiguration_RequiresKeyModelAndSupportedEffort()
    {
        var options = Options(); options.ApiKey = ""; Assert.Throws<InvalidOperationException>(options.Validate);
        options = Options(); options.Model = ""; Assert.Throws<InvalidOperationException>(options.Validate);
        options = Options(); options.ReasoningEffort = "none"; Assert.Throws<InvalidOperationException>(options.Validate);
        options = Options(); options.MaxOutputTokens = 0; Assert.Throws<InvalidOperationException>(options.Validate);
    }

    internal static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
}