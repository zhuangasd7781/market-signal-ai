using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true", ["Storage:Provider"] = "Memory"
        }));
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
}
