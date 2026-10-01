using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Xunit;
namespace MarketSignalAI.Tests;
public sealed class DeepSeekAnalystTests
{
    internal static DeepSeekAnalystOptions Options() => new() { Enabled = true, ApiKey = "test-deepseek-key", Model = "configured-deepseek", BaseUrl = "https://api.deepseek.com/v1/" };
    internal static string Response(string? content = null, string finish = "stop", bool detailedCache = false) => JsonSerializer.Serialize(new
    {
        model = "reported-deepseek", choices = new[] { new { finish_reason = finish, message = new { content = content ?? JsonSerializer.Serialize(DemoData.MockResult("HOLD", true), new JsonSerializerOptions(JsonSerializerDefaults.Web)) } } },
        usage = detailedCache ? (object)new { prompt_tokens = 1500, completion_tokens = 600, prompt_tokens_details = new { cached_tokens = 128 } } : new { prompt_tokens = 1500, completion_tokens = 600, prompt_cache_hit_tokens = 128 }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesSharedContextSkillsSchemaAndActualUsage(bool detailedCache)
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler(async (request, ct) =>
        {
            Assert.Equal("https://api.deepseek.com/v1/chat/completions", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            Assert.Equal("configured-deepseek", root.GetProperty("model").GetString());
            Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
            var messages = root.GetProperty("messages");
            Assert.Contains("daily rebalancing", messages[0].GetProperty("content").GetString()!);
            Assert.Contains("nextActions", messages[0].GetProperty("content").GetString()!);
            using var input = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            Assert.Equal(6, input.RootElement.GetProperty("position").GetProperty("quantity").GetDecimal());
            Assert.False(input.RootElement.GetProperty("position").TryGetProperty("userId", out _));
            Assert.Single(input.RootElement.GetProperty("history").EnumerateArray());
            return OpenAIAnalystTests.Json(Response(detailedCache: detailedCache));
        }));
        var result = await new DeepSeekAnalyst(http, Options()).AnalyzeAsync(OpenAIAnalystTests.Context(), default);
        Assert.Equal("reported-deepseek", result.Model);
        Assert.Equal(new TokenUsage(1500, 600, 128), result.Usage);
    }
    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-confidence")]
    [InlineData("missing-nextActions")]
    [InlineData("extra")]
    [InlineData("enum")]
    [InlineData("position")]
    [InlineData("truncated")]
    [InlineData("null-root")]
    public async Task InvalidResponsesFailAndPreserveUsage(string scenario)
    {
        var node = JsonSerializer.SerializeToNode(DemoData.MockResult("HOLD", true), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        switch (scenario)
        {
            case "missing-confidence": node.AsObject().Remove("confidence"); break;
            case "missing-nextActions": node.AsObject().Remove("nextActions"); break;
            case "extra": node["extra"] = true; break;
            case "enum": node["trend"] = "invented"; break;
            case "position": node["action"] = "REDUCE"; node["quantity"] = 7; break;
            case "null-root": node["rootEvent"] = null; break;
        }
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((_, _) => Task.FromResult(OpenAIAnalystTests.Json(Response(scenario == "malformed" ? "not JSON" : node.ToJsonString(), scenario == "truncated" ? "length" : "stop")))));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => new DeepSeekAnalyst(http, Options()).AnalyzeAsync(OpenAIAnalystTests.Context(), default));
        Assert.Equal(new TokenUsage(1500, 600, 128), error.Usage);
    }
    [Theory]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpFailureDoesNotLeakBody(int status)
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("secret body") })));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => new DeepSeekAnalyst(http, Options()).AnalyzeAsync(OpenAIAnalystTests.Context(), default));
        Assert.Contains(status.ToString(), error.Message); Assert.DoesNotContain("secret body", error.ToString());
    }
    [Fact]
    public async Task CancellationPropagates()
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return OpenAIAnalystTests.Json(Response()); }));
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DeepSeekAnalyst(http, Options()).AnalyzeAsync(OpenAIAnalystTests.Context(), ct.Token));
    }
}
