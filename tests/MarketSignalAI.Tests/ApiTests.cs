using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Api;
using MarketSignalAI.Infrastructure;
using MarketSignalAI.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class ApiFactory(Action<IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("DeepSeek:Enabled", "false").UseEnvironment("Development").UseSetting("OpenAI:Enabled", "false")
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true", ["Storage:Provider"] = "Memory", ["OpenAI:Enabled"] = "false"
        })).ConfigureTestServices(s => {
            // Existing API fixtures explicitly opt into mocks, never real external requests.
            s.RemoveAll<IAIAnalyst>();
            foreach(var code in new[]{"openai","deepseek","claude"})s.AddSingleton<IAIAnalyst>(new MockAIAnalyst(code));
            s.RemoveAll<ITwMarketContextProvider>();
            s.AddSingleton<ITwMarketContextProvider, OfflineTwMarketContextProvider>();
            s.RemoveAll<IAIProviderSettingsStore>();
            s.AddSingleton<IAIProviderSettingsStore>(new MemoryAIProviderSettingsStore([new("openai",true,"test-openai"),new("deepseek",true,"test-deepseek"),new("claude",true,"mock-v1")]));
            services?.Invoke(s);
        });
}

public sealed class ApiTests
{
    [Fact]
    public async Task Watchlist_ContainsDynamicProviders_AndChangesFromHistory()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var board = await client.GetFromJsonAsync<WatchlistResponse>("/api/watchlist");
        Assert.NotNull(board);
        Assert.Equal(3, board.Providers.Count);
        var row = Assert.Single(board.Items, x => x.Product.Symbol == "00631L");
        Assert.True(Assert.Single(row.Signals, x => x.Provider == "openai").Changed);
        Assert.False(Assert.Single(row.Signals, x => x.Provider == "deepseek").Changed);
        var json = await client.GetStringAsync("/api/watchlist");
        Assert.DoesNotContain("averageCost", json);
        Assert.DoesNotContain("rawResponse", json);
    }

    [Fact]
    public async Task Watchlist_Deduplicates_AndValidatesProducts()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/watchlist", new { productId = 4 })).StatusCode);
        await client.PostAsJsonAsync("/api/watchlist", new { productId = 4 });
        var board = await client.GetFromJsonAsync<WatchlistResponse>("/api/watchlist");
        Assert.Single(board!.Items, x => x.Product.Id == 4);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/watchlist", new { productId = 999 })).StatusCode);
        await client.DeleteAsync("/api/watchlist/4");
        board = await client.GetFromJsonAsync<WatchlistResponse>("/api/watchlist");
        Assert.DoesNotContain(board!.Items, x => x.Product.Id == 4);
    }

    [Fact]
    public async Task Position_RejectsInvalidOrUntrackedInput_AndIgnoresClientUserId()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/products/2330/position", new { quantity = -1, averageCost = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/products/2330/position", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/products/AAPL/position?market=US", new { quantity = 1, averageCost = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/products/2330/position", new { userId = 999, quantity = 2, averageCost = 100 })).StatusCode);
        var json = await client.GetFromJsonAsync<JsonElement>("/api/products/2330/position");
        Assert.Equal(1, json.GetProperty("position").GetProperty("userId").GetInt32());
    }

    [Fact]
    public async Task Store_IsolatesUsers_AndKeepsHistoryAfterUntracking()
    {
        var store = new MemorySignalStore();
        Assert.Empty(await store.GetWatchlistAsync(2, default));
        Assert.Empty(await store.GetHistoryAsync(2, null, default));
        await store.SavePositionAsync(new(1, 1, 6, 33.74m, DateTime.UtcNow), default);
        Assert.Null(await store.GetPositionAsync(2, 1, default));
        var before = await store.GetHistoryAsync(1, 1, default);
        await store.RemoveWatchAsync(2, 1, default);
        Assert.Equal(3, (await store.GetWatchlistAsync(1, default)).Count);
        await store.RemoveWatchAsync(1, 1, default);
        Assert.Equal(before.Count, (await store.GetHistoryAsync(1, 1, default)).Count);
    }

    [Fact]
    public async Task Mutations_RequireBrowserHeader_AndHistoryHidesRawPayload()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/watchlist", new { productId = 4 })).StatusCode);
        var history = await client.GetStringAsync("/api/products/2330/analysis/history");
        Assert.DoesNotContain("rawResponse", history);
        Assert.DoesNotContain("inputSnapshotJson", history);
        Assert.Equal(6, JsonDocument.Parse(history).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task MarketApi_ReadsStoredSnapshot_AndForceAppendsAnalysis()
    {
        var fake = new FakeMarketDataProvider();
        await using var factory = new ApiFactory(s => { s.RemoveAll<IMarketDataProvider>(); s.AddSingleton<IMarketDataProvider>(fake); });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/products/00631L/market")).StatusCode);
        Assert.Equal(0, fake.SnapshotCalls);
        var force = await client.PostAsync("/api/products/00631L/analysis/force", null);
        Assert.Equal(HttpStatusCode.OK, force.StatusCode);
        var run = await force.Content.ReadFromJsonAsync<ProductRunResult>();
        Assert.Equal(3, run!.Providers.Count);
        Assert.All(run.Providers, p => Assert.Equal("COMPLETED", p.Status));
        Assert.All(run.Providers, p => { Assert.Equal("mock-v1", p.Model); Assert.NotNull(p.Result); });
        var quote = await client.GetFromJsonAsync<MarketResponse>("/api/products/00631L/market");
        Assert.Equal(42.5m, quote!.Price);
        Assert.Equal(1, fake.SnapshotCalls);
        var history = await client.GetFromJsonAsync<AnalysisView[]>("/api/products/00631L/analysis/history");
        Assert.Equal(9, history!.Length);
        Assert.Equal(3, history.Count(x => x.Model == "mock-v1" && x.CreatedAt > DateTime.UtcNow.AddMinutes(-2)));
        await client.PostAsync("/api/products/00631L/analysis/force", null);
        history = await client.GetFromJsonAsync<AnalysisView[]>("/api/products/00631L/analysis/history");
        Assert.Equal(12, history!.Length);
    }

    [Fact]
    public async Task BatchAndProviderFailures_DoNotLoseOtherResults()
    {
        var fake = new FakeMarketDataProvider { FailingSymbol = "5871" };
        await using var factory = new ApiFactory(s =>
        {
            s.RemoveAll<IMarketDataProvider>(); s.AddSingleton<IMarketDataProvider>(fake);
            s.RemoveAll<IAIAnalyst>();
            s.AddSingleton<IAIAnalyst>(new MockAIAnalyst("deepseek"));
            s.AddSingleton<IAIAnalyst>(new MockAIAnalyst("openai"));
            s.AddSingleton<IAIAnalyst>(new FailingAnalyst());
        });
        using var scope = factory.Services.CreateScope();
        var batch = await scope.ServiceProvider.GetRequiredService<IMarketAnalysisRunner>().RunAllAsync(default);
        Assert.Equal(2, batch.Completed.Count);
        Assert.Single(batch.Failed, x => x.Symbol == "5871");
        Assert.All(batch.Completed, p =>
        {
            Assert.Equal(2, p.Providers.Count(x => x.Status == "COMPLETED"));
            Assert.Single(p.Providers, x => x.Status == "FAILED" && x.Provider == "claude");
        });
    }

    [Fact]
    public async Task Swagger_ContainsAllFastEndpointRoutes()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/products/{symbol}/market", out _));
        Assert.True(paths.TryGetProperty("/api/products/{symbol}/analysis/force", out _));
        Assert.True(paths.TryGetProperty("/api/watchlist", out _));
        var parameters = paths.GetProperty("/api/products/{symbol}/analysis/force").GetProperty("post").GetProperty("parameters");
        Assert.Contains(parameters.EnumerateArray(), p => p.GetProperty("name").GetString() == "X-Market-Signal" && p.GetProperty("in").GetString() == "header");
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        public int SnapshotCalls { get; private set; }
        public string? FailingSymbol { get; init; }
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct)
        {
            SnapshotCalls++;
            if (symbol == FailingSymbol) throw new HttpRequestException("simulated Yahoo failure");
            return Task.FromResult(new MarketSnapshot(symbol, 42.5m, 42m, 43m, 41m, 40m, 1234,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HistoricalPrice>>([]);
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class FailingAnalyst : IAIAnalyst
    {
        public string ProviderCode => "claude";
        public Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct) => throw new InvalidDataException("simulated provider failure");
    }
}
