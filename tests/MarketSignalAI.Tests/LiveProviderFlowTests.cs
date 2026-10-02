using System.Net;
using System.Net.Http.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace MarketSignalAI.Tests;
public sealed class LiveProviderFlowTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("deepseek")]
    public async Task EitherFailureKeepsOtherLiveResult(string failed)
    {
        await using var factory = new Factory(failed);
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        var response = await client.PostAsync("/api/products/00631L/analysis/force", null); response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ProductRunResult>())!;
        var failure = Assert.Single(run.Providers, p => p.Provider == failed);
        Assert.Equal("FAILED", failure.Status); Assert.Null(failure.Result); Assert.Null(failure.AnalysisId);
        var success = Assert.Single(run.Providers, p => p.Provider == (failed == "openai" ? "deepseek" : "openai"));
        Assert.Equal("COMPLETED", success.Status); Assert.NotNull(success.Usage);
        Assert.Equal("mock-v1", Assert.Single(run.Providers, p => p.Provider == "claude").Model);
        var store = Assert.IsType<MemoryMarketStore>(factory.Services.GetRequiredService<IMarketStore>()); Assert.Single(store.Failures);
        var records = await factory.Services.GetRequiredService<ISignalStore>().GetHistoryAsync(1, 1, default);
        Assert.Equal(success.Usage, Assert.Single(records, r => r.Id == success.AnalysisId).Usage);
        var publicJson = await client.GetStringAsync("/api/products/00631L/analysis");
        Assert.DoesNotContain("rawResponse", publicJson); Assert.DoesNotContain("test-api-key", publicJson);
    }
    private sealed class Factory(string failed) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development")
            .UseSetting("Demo:Enabled", "true").UseSetting("Storage:Provider", "Memory")
            .UseSetting("OpenAI:Enabled", "true").UseSetting("OpenAI:ApiKey", "test-api-key")
            .UseSetting("DeepSeek:Enabled", "true").UseSetting("DeepSeek:ApiKey", "test-deepseek-key")
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IAIProviderSettingsStore>();
                s.AddSingleton<IAIProviderSettingsStore>(new MemoryAIProviderSettingsStore([new("openai",true,"gpt-6.1-sol"),new("deepseek",true,"deepseek-v4-pro"),new("claude",true,"mock-v1")]));
                s.RemoveAll<IMarketDataProvider>(); s.AddSingleton<IMarketDataProvider, Market>();
                s.RemoveAll<ITwMarketContextProvider>(); s.AddSingleton<ITwMarketContextProvider, OfflineTwMarketContextProvider>();
                s.AddHttpClient<OpenAIAnalyst>().ConfigurePrimaryHttpMessageHandler(() => new OpenAIAnalystTests.Handler((_, _) => Task.FromResult(failed == "openai" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : OpenAIAnalystTests.Json(OpenAIAnalystTests.Response()))));
                s.AddHttpClient<DeepSeekAnalyst>().ConfigurePrimaryHttpMessageHandler(() => new OpenAIAnalystTests.Handler((_, _) => Task.FromResult(failed == "deepseek" ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response()))));
            });
    }
    private sealed class Market : IMarketDataProvider
    {
        public Task<MarketSignalAI.Domain.MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().Snapshot);
        public Task<IReadOnlyList<MarketSignalAI.Domain.HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().History);
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true);
    }
}
