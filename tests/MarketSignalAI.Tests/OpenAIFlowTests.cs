using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using MarketSignalAI.Api;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class OpenAIFlowTests
{
    [Fact]
    public async Task Force_UsesRealAdapterRegistration_StoresUsageAndInstructions_AndKeepsOtherMocks()
    {
        await using var factory = new PipelineFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        var options = factory.Services.GetRequiredService<OpenAIAnalystOptions>();
        Assert.Equal("gpt-6.1-sol", options.Model);
        Assert.Equal("medium", options.ReasoningEffort);
        var response = await client.PostAsync("/api/products/00631L/analysis/force", null);
        response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ProductRunResult>())!;
        var gpt = Assert.Single(run.Providers, x => x.Provider == "openai");
        Assert.Equal("COMPLETED", gpt.Status);
        Assert.Equal("gpt-6.1-sol", gpt.Model);
        Assert.Equal(new TokenUsage(1234, 567), gpt.Usage);
        Assert.All(run.Providers.Where(x => x.Provider != "openai"), p => { Assert.Equal("mock-v1", p.Model); Assert.Null(p.Usage); });
        var records = await factory.Services.GetRequiredService<ISignalStore>().GetHistoryAsync(1, 1, default);
        var saved = Assert.Single(records, x => x.Id == gpt.AnalysisId);
        Assert.Equal(gpt.Usage, saved.Usage);
        using var input = JsonDocument.Parse(saved.InputSnapshotJson);
        Assert.False(input.RootElement.GetProperty("isMock").GetBoolean());
        Assert.Equal("medium", input.RootElement.GetProperty("reasoningEffort").GetString());
        Assert.NotEmpty(input.RootElement.GetProperty("instructions").GetString()!);
        Assert.Equal(JsonValueKind.Null, input.RootElement.GetProperty("previousDecision").ValueKind);
        var history = await client.GetFromJsonAsync<AnalysisView[]>("/api/products/00631L/analysis/history");
        Assert.Equal(gpt.Usage, Assert.Single(history!, x => x.Id == gpt.AnalysisId).Usage);
        var publicJson = await client.GetStringAsync("/api/products/00631L/analysis/history");
        Assert.DoesNotContain("test-api-key", publicJson);
        Assert.DoesNotContain("rawResponse", publicJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Force_FailureIsPersisted_WithoutFallbackOrLosingOtherProviders(bool incomplete)
    {
        await using var factory = new PipelineFactory(fail: true, incomplete: incomplete);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        var response = await client.PostAsync("/api/products/00631L/analysis/force", null);
        response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ProductRunResult>())!;
        var gpt = Assert.Single(run.Providers, x => x.Provider == "openai");
        Assert.Equal("FAILED", gpt.Status);
        Assert.Null(gpt.AnalysisId);
        Assert.Null(gpt.Result);
        Assert.Equal("gpt-6.1-sol", gpt.Model);
        Assert.Equal(2, run.Providers.Count(x => x.Status == "COMPLETED"));
        var records = await factory.Services.GetRequiredService<ISignalStore>().GetHistoryAsync(1, 1, default);
        Assert.Equal(8, records.Count);
        var store = Assert.IsType<MemoryMarketStore>(factory.Services.GetRequiredService<IMarketStore>());
        var failure = Assert.Single(store.Failures);
        Assert.Equal("gpt-6.1-sol", failure.Model);
        Assert.Equal("medium", failure.ReasoningEffort);
        Assert.Equal(incomplete ? new TokenUsage(1234, 567) : null, failure.Usage);
        Assert.Equal(failure.Usage, gpt.Usage);
    }

    [MySqlFact]
    public async Task MySql_PersistsUsageAndFailures_AcrossApiRestart()
    {
        var connection = Environment.GetEnvironmentVariable("MARKET_SIGNAL_TEST_MYSQL")!;
        var settings = new MySqlConnectionStringBuilder(connection);
        Assert.StartsWith("market_signal_test_", settings.Database);
        await using var db = new MySqlConnection(connection);
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "001_initial.sql")));
        long analysisId;
        await using (var factory = new PipelineFactory(connection: connection))
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
            var response = await client.PostAsync("/api/products/00631L/analysis/force", null);
            response.EnsureSuccessStatusCode();
            var run = (await response.Content.ReadFromJsonAsync<ProductRunResult>())!;
            analysisId = Assert.Single(run.Providers, x => x.Provider == "openai").AnalysisId!.Value;
        }
        await using (var restarted = new PipelineFactory(connection: connection, fail: true, incomplete: true))
        {
            using var client = restarted.CreateClient();
            var history = (await client.GetFromJsonAsync<AnalysisView[]>("/api/products/00631L/analysis/history"))!;
            Assert.Equal(9, history.Length);
            Assert.Equal(new TokenUsage(1234, 567), Assert.Single(history, x => x.Id == analysisId).Usage);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products/00631L/market?market=TW")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
            var response = await client.PostAsync("/api/products/00631L/analysis/force", null);
            response.EnsureSuccessStatusCode();
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AIProviderFailures WHERE Model='gpt-6.1-sol' AND InputTokens=1234 AND OutputTokens=567"));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AIAnalysisUsage WHERE InputTokens=1234 AND OutputTokens=567"));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AIAnalysisResults WHERE Model='gpt-6.1-sol'"));
            var settingDefaults=new AIProviderSetting[]{new("openai",true,"gpt-6.1-sol"),new("deepseek",true,"deepseek-v4-pro"),new("claude",false,"mock-v1")};
            var providerSettings=new MySqlAIProviderSettingsStore(connection,settingDefaults);
            await providerSettings.MigrateAndSeedAsync(default);
            await providerSettings.SaveAsync(1,new("openai",false,"updated-model"),default);
            var settingsRestarted=new MySqlAIProviderSettingsStore(connection,settingDefaults);
            await settingsRestarted.MigrateAndSeedAsync(default);
            var persisted=Assert.Single(await settingsRestarted.GetAsync(1,default),x=>x.Provider=="openai");
            Assert.False(persisted.Enabled);Assert.Equal("updated-model",persisted.ConfiguredModel);
            Assert.True(Assert.Single(await settingsRestarted.GetAsync(2,default),x=>x.Provider=="openai").Enabled);
            // Exercise the actual MySQL trading-day mapping/upsert and tracked-product batch.
            using var scope = restarted.Services.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IMarketScheduleExecutor>();
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei")).DateTime);
            await executor.ExecuteAsync(date, true, default);
            await executor.ExecuteAsync(date, false, default);
            var store = scope.ServiceProvider.GetRequiredService<IMarketStore>();
            Assert.Equal("OPEN", (await store.GetTradingDayAsync(date, "TW", default))!.Status);
            Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AIProviderFailures"));
            var record = (await restarted.Services.GetRequiredService<ISignalStore>().GetHistoryAsync(1, 1, default)).Single(x => x.Id == analysisId);
            var cachedRecord = await store.SaveAnalysisAsync(record with { Id = 0, Usage = new TokenUsage(1234, 567, 128), CreatedAt = DateTime.UtcNow }, default);
            Assert.Equal(128, await db.ExecuteScalarAsync<long>("SELECT CachedTokens FROM AIAnalysisUsage WHERE AnalysisId=@Id", new { cachedRecord.Id }));
            var readBack = (await restarted.Services.GetRequiredService<ISignalStore>().GetHistoryAsync(1, 1, default)).Single(x => x.Id == cachedRecord.Id);
            Assert.Equal(cachedRecord.Usage, readBack.Usage);
            await store.SaveProviderFailureAsync(new(1, 1, 1, "deepseek-chat", null, "test validation failure", new(1500, 600, 128), DateTime.UtcNow), default);
            Assert.Equal(128, await db.ExecuteScalarAsync<long>("SELECT CachedTokens FROM AIProviderFailures WHERE Model='deepseek-chat' ORDER BY Id DESC LIMIT 1"));
            await ((MySqlMarketStore)store).MigrateAsync(default); // Existing-column path must be idempotent.
            var referenceStore=restarted.Services.GetRequiredService<MySqlMarketReferenceStore>();
            var seeded=await referenceStore.GetReferencesAsync(1,1,default);Assert.Equal(2,seeded.Count);
            Assert.Contains(seeded,x=>x.ReferenceType=="UNDERLYING" && x.Instrument.Symbol=="^TSE50");
            Assert.Empty(await referenceStore.GetReferencesAsync(999,1,default));
            var extra=await referenceStore.SaveInstrumentAsync(1,null,"^SOX","SOX","US",default);
            var mapping=await referenceStore.SaveReferenceAsync(1,1,null,extra.Id,"SECTOR",default);
            await referenceStore.SaveReferenceAsync(1,1,null,seeded[0].Instrument.Id,"SECTOR",default);
            await Assert.ThrowsAsync<KeyNotFoundException>(()=>referenceStore.SaveReferenceAsync(999,1,null,extra.Id,"SECTOR",default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>referenceStore.DeleteInstrumentAsync(1,extra.Id,default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>referenceStore.SaveReferenceAsync(1,1,null,extra.Id,"SECTOR",default));
            await referenceStore.SaveInstrumentAsync(1,extra.Id,"^SOX","Updated SOX","US",default);
            await referenceStore.SaveReferenceAsync(1,1,mapping.Id,extra.Id,"BROAD_MARKET",default);
            await referenceStore.DeleteReferenceAsync(1,1,mapping.Id,default);await referenceStore.DeleteInstrumentAsync(1,extra.Id,default);
            await referenceStore.DeleteReferenceAsync(1,1,seeded[0].Id,default);
            await referenceStore.MigrateAsync(default);await referenceStore.SeedAsync(default);
            Assert.DoesNotContain(await referenceStore.GetReferencesAsync(1,1,default),x=>x.Id==seeded[0].Id);


        }
    }

    private sealed class PipelineFactory(bool fail = false, bool incomplete = false, string? connection = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("DeepSeek:Enabled", "false").UseEnvironment("Development")
            .UseSetting("OpenAI:Enabled", "true")
            .UseSetting("OpenAI:ApiKey", "test-api-key")
            .UseSetting("Storage:Provider", connection is null ? "Memory" : "MySql")
            .UseSetting("ConnectionStrings:MySql", connection ?? "")
            .UseSetting("MarketWorker:Enabled", "false")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true", ["Storage:Provider"] = connection is null ? "Memory" : "MySql",
                ["ConnectionStrings:MySql"] = connection, ["MarketWorker:Enabled"] = "false",
                ["OpenAI:Enabled"] = "true", ["OpenAI:ApiKey"] = "test-api-key"
            })).ConfigureTestServices(services =>
            {
                services.RemoveAll<IAIProviderSettingsStore>();
                services.AddSingleton<IAIProviderSettingsStore>(new MemoryAIProviderSettingsStore([new("openai",true,"gpt-6.1-sol"),new("deepseek",true,"test-deepseek"),new("claude",true,"mock-v1")]));
                services.RemoveAll<IAIAnalyst>();
                services.AddTransient<IAIAnalyst>(sp=>sp.GetRequiredService<OpenAIAnalyst>());
                services.AddSingleton<IAIAnalyst>(new MockAIAnalyst("deepseek"));
                services.AddSingleton<IAIAnalyst>(new MockAIAnalyst("claude"));
                services.RemoveAll<IMarketDataProvider>(); services.AddSingleton<IMarketDataProvider, TestMarket>();
                services.AddHttpClient<OpenAIAnalyst>().ConfigurePrimaryHttpMessageHandler(() => new OpenAIAnalystTests.Handler((_, _) =>
                    Task.FromResult(fail && !incomplete ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) :
                        OpenAIAnalystTests.Json(OpenAIAnalystTests.Response(incomplete ? "incomplete" : "completed")))));
            });
    }
    private sealed class TestMarket : IMarketDataProvider
    {
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().Snapshot with { Symbol = symbol });
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => Task.FromResult(OpenAIAnalystTests.Context().History);
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true);
    }
}

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MARKET_SIGNAL_TEST_MYSQL")))
            Skip = "Run scripts/test-backend-mysql.ps1 for isolated MySQL integration verification.";
    }
}