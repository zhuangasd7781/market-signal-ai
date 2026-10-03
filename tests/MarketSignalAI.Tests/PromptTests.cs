using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;

public sealed class PromptTests
{
    [Fact]
    public async Task VersionsAreImmutableUserScopedAndCanReactivateOldContent()
    {
        var store = new MemoryPromptStore();
        var initial = await store.GetAsync(1, default);
        var seed = Assert.Single(initial.Versions);
        var second = await store.CreateAsync(1, "Changed risk guidance", default);
        Assert.Equal("investment-analysis-v2", second.Version);
        var changed = await store.GetAsync(1, default);
        Assert.Equal(second.Id, changed.ActiveVersionId);
        Assert.Equal(seed, Assert.Single(changed.Versions, x => x.Id == seed.Id));
        var other = await store.GetAsync(2, default);
        Assert.Single(other.Versions);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ActivateAsync(2, second.Id, default));
        await store.ActivateAsync(1, seed.Id, default);
        Assert.Equal(seed.Id, (await store.GetAsync(1, default)).ActiveVersionId);
        Assert.Equal(2, (await store.GetAsync(1, default)).Versions.Count);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyPromptCannotReplaceActiveVersion(string content)
    {
        var store = new MemoryPromptStore();
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(1, content, default));
        Assert.Single((await store.GetAsync(1, default)).Versions);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(1, new string('a', 30001), default));
    }
    [Fact]
    public async Task PromptApiRequiresMutationHeaderAndNeverCallsAI()
    {
        var forbidden = new ForbiddenAnalyst();
        await using var factory = new ApiFactory(s => { s.RemoveAll<IAIAnalyst>(); s.AddSingleton<IAIAnalyst>(forbidden); });
        using var client = factory.CreateClient();
        var initial = (await client.GetFromJsonAsync<PromptSettings>("/api/prompts"))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/prompts", new { content = "new guidance" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/prompts", new { content = "" })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/prompts", new { userId = 999, content = "New core guidance" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var version = (await response.Content.ReadFromJsonAsync<PromptVersion>())!;
        Assert.Equal(version.Id, (await client.GetFromJsonAsync<PromptSettings>("/api/prompts"))!.ActiveVersionId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/prompts/active", new { versionId = initial.ActiveVersionId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/prompts/active", new { versionId = 99999 })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync("/api/prompts", new { content = "overwrite" })).StatusCode);
        Assert.Equal(0, forbidden.Calls);
    }
    [Fact]
    public async Task SharedPromptIsFrozenDuringProviderExecutionAndHistoryRetainsBothVersions()
    {
        var prompts = new MemoryPromptStore();
        var seed = (await prompts.GetAsync(1, default)).Versions.Single();
        var inputs = new List<MarketContext>();
        var first = new CapturingAnalyst("deepseek", async context => {
            inputs.Add(context);
            if (context.Prompt!.Version == seed.Version) await prompts.CreateAsync(1, "Second version core rules", default);
        });
        var second = new CapturingAnalyst("openai", context => { inputs.Add(context); return Task.CompletedTask; });
        var signals = new MemorySignalStore(); var market = new MemoryMarketStore(signals);
        var runner = AnalysisRunnerFixture.Create(new Market(), market, signals, [first, second], NullLogger<MarketAnalysisRunner>.Instance, prompts: prompts);
        var run1 = await runner.RunProductAsync("00631L", default);
        var run2 = await runner.RunProductAsync("00631L", default);
        Assert.Same(inputs[0], inputs[1]); Assert.Same(inputs[2], inputs[3]);
        Assert.Equal(seed.Content, inputs[1].Prompt!.Content);
        Assert.Equal("investment-analysis-v2", inputs[3].Prompt!.Version);
        var records = await signals.GetHistoryAsync(1, 1, default);
        foreach (var run in new[] { run1, run2 })
        {
            foreach (var row in run.Providers.Where(x => x.AnalysisId.HasValue))
            {
                using var json = JsonDocument.Parse(records.Single(x => x.Id == row.AnalysisId).InputSnapshotJson);
                Assert.Equal(run == run1 ? seed.Version : "investment-analysis-v2", json.RootElement.GetProperty("promptVersion").GetString());
                Assert.Equal(run == run1 ? seed.Content : "Second version core rules", json.RootElement.GetProperty("promptSnapshot").GetProperty("Content").GetString());
                var skills = json.RootElement.GetProperty("skillIdentifiers").EnumerateArray().Select(x => x.GetString()).ToArray();
                Assert.Equal(4, skills.Length);
                Assert.Contains(skills, x => x!.StartsWith("news-evidence:"));
                Assert.Contains(skills, x => x!.StartsWith("tw-market-context:", StringComparison.Ordinal));
            }
        }
        var views = await new SignalService(signals, new User()).GetAnalysisAsync(1, true, default);
        Assert.Contains(views, x => x.PromptVersion == seed.Version);
        Assert.Contains(views, x => x.PromptVersion == "investment-analysis-v2");
        Assert.Equal(inputs[3].Snapshot.Price, views.First(x => x.PromptVersion == "investment-analysis-v2").Context!.TargetPrice);
        var serialized = JsonSerializer.Serialize(views);
        Assert.DoesNotContain("RawResponse", serialized); Assert.DoesNotContain("InputSnapshotJson", serialized);
        Assert.DoesNotContain("Second version core rules", serialized);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("broken old snapshot")]
    [InlineData("null")]
    public async Task LegacyOrMalformedInputDoesNotBreakHistory(string snapshot)
    {
        var signals = new MemorySignalStore();
        signals.AddAnalysis(new(0, 1, 1, 1, "legacy", DemoData.MockResult("HOLD", true), snapshot, "secret raw provider data", DateTime.UtcNow));
        var view = (await new SignalService(signals, new User()).GetAnalysisAsync(1, true, default)).First(x => x.Model == "legacy");
        Assert.Null(view.PromptVersion); Assert.Null(view.Context);
    }
    [Fact]
    public async Task BothRealAdaptersSendSharedCustomCorePromptWithoutChangingSchema()
    {
        var store = new MemoryPromptStore(); var version = await store.CreateAsync(1, "Custom risk rule: assess liquidity first.", default);
        var instructions = new List<string>();
        var inputs = new List<string>();
        using var openHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (req, ct) => {
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            instructions.Add(body.RootElement.GetProperty("instructions").GetString()!);
            inputs.Add(body.RootElement.GetProperty("input").GetString()!);
            Assert.Equal("object", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("type").GetString());
            return OpenAIAnalystTests.Json(OpenAIAnalystTests.Response());
        }));
        using var deepHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (req, ct) => {
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            instructions.Add(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!);
            inputs.Add(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());
        }));
        var signals = new MemorySignalStore(); var runner = AnalysisRunnerFixture.Create(new Market(), new MemoryMarketStore(signals), signals,
            [new OpenAIAnalyst(openHttp, OpenAIAnalystTests.Options()), new DeepSeekAnalyst(deepHttp, DeepSeekAnalystTests.Options())], NullLogger<MarketAnalysisRunner>.Instance, prompts: store);
        var run = await runner.RunProductAsync("00631L", default);
        Assert.Equal(2, run.Providers.Count(x => x.Status == "COMPLETED")); Assert.All(run.Providers.Where(x => x.Status == "COMPLETED"), x => Assert.Equal("COMPLETED", x.Status));
        Assert.Equal(2, inputs.Count); Assert.Equal(inputs[0], inputs[1]);
        Assert.StartsWith(instructions[1], instructions[0]);
        Assert.All(instructions, x => { Assert.StartsWith(version.Content, x); Assert.Contains("system JSON schema", x); Assert.Contains("NO positive position", x); });
    }
    internal static async Task AssertMySqlPromptHistoryAsync(string connection)
    {
        await using var db = new MySqlConnector.MySqlConnection(connection);
        await db.OpenAsync();
        await using var addUser = db.CreateCommand(); addUser.CommandText = "INSERT IGNORE INTO Users(Id,DisplayName,Email) VALUES(902,'Prompt test user','prompt-test-902@example.invalid')"; await addUser.ExecuteNonQueryAsync();
        var store = new MySqlPromptStore(connection); await store.MigrateAsync(default);
        var initial = await store.GetAsync(1, default);
        var created = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => store.CreateAsync(1, $"Concurrent prompt {i}", default)));
        Assert.Equal(4, created.Select(x => x.Version).Distinct().Count());
        var restarted = new MySqlPromptStore(connection); await restarted.MigrateAsync(default);
        var after = await restarted.GetAsync(1, default);
        Assert.Equal(initial.Versions.Count + 4, after.Versions.Count);
        Assert.Contains(after.Versions, x => x.Id == after.ActiveVersionId && created.Any(y => y.Id == x.Id));
        Assert.Equal(initial.Versions.Single(x => x.Id == initial.ActiveVersionId), after.Versions.Single(x => x.Id == initial.ActiveVersionId));
        await restarted.ActivateAsync(1, initial.ActiveVersionId, default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => restarted.ActivateAsync(902, created[0].Id, default));
    }
    private sealed class User : ICurrentUser { public long UserId => 1; }
    private sealed class ForbiddenAnalyst : IAIAnalyst
    { public int Calls; public string ProviderCode => "openai"; public Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct) { Calls++; throw new InvalidOperationException("Prompt settings must never call AI"); } }
    private sealed class CapturingAnalyst(string code, Func<MarketContext, Task> capture) : IAIAnalyst
    { public string ProviderCode => code; public async Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct) { await capture(context); return new("reported-" + code, DemoData.MockResult("HOLD", true), "{}", Usage: new(100, 50)); } }
    private sealed class Market : IMarketDataProvider
    { public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().Snapshot with { Symbol = symbol }); public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().History); public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true); }
}
