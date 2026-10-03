using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Api;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;

public sealed class AIProviderSettingsTests
{
    private static AIProviderSetting[] Defaults(bool gpt=true,bool deep=true)=>[new("openai",gpt,"configured-gpt"),new("deepseek",deep,"configured-deep"),new("claude",false,"mock-v1")];
    [Fact]
    public async Task SettingsGetSaveAreScopedAndNeverExecuteAnalysts()
    {
        var forbidden=new ThrowingAnalyst();
        await using var factory=new ApiFactory(s=>{s.RemoveAll<IAIAnalyst>();s.AddSingleton<IAIAnalyst>(forbidden);});
        using var client=factory.CreateClient();
        var initial=await client.GetFromJsonAsync<AIProviderSettingView[]>("/api/ai/settings");
        Assert.Equal(3,initial!.Length);Assert.NotNull(initial[0].ActualModel);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("/api/ai/settings/openai",new {enabled=false,configuredModel="updated-model"})).StatusCode);
        client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/ai/settings/openai",new {enabled=false,configuredModel="updated-model"})).StatusCode);
        var store=factory.Services.GetRequiredService<IAIProviderSettingsStore>();
        Assert.False(Assert.Single(await store.GetAsync(1,default),x=>x.Provider=="openai").Enabled);
        Assert.True(Assert.Single(await store.GetAsync(2,default),x=>x.Provider=="openai").Enabled);
        var json=await client.GetStringAsync("/api/ai/settings");Assert.DoesNotContain("apiKey",json,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0,forbidden.Calls);
    }
    [Theory]
    [InlineData("claude","real-claude")]
    [InlineData("openai","mock-v1")]
    [InlineData("deepseek","")]
    [InlineData("unknown","model")]
    [InlineData("deepseek","model with spaces")]
    public async Task InvalidSettingsAreRejectedWithoutMutation(string provider,string model)
    {
        await using var factory=new ApiFactory();using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/ai/settings/"+provider,new {enabled=true,configuredModel=model})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/ai/settings/openai",new {configuredModel="model"})).StatusCode);
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task ForceAndBatchOnlyCallEnabledSelectedProvidersWithConfiguredModel(bool gptEnabled,bool batch)
    {
        var gptCalls=0;var deepCalls=0;
        using var gptHttp=new HttpClient(new OpenAIAnalystTests.Handler((_,_)=>{gptCalls++;throw new InvalidOperationException("GPT must not be called");}));
        using var deepHttp=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>{
            deepCalls++;using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            Assert.Equal("configured-deep",body.RootElement.GetProperty("model").GetString());
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());
        }));
        var signals=new MemorySignalStore();var marketStore=new MemoryMarketStore(signals);var settings=new MemoryAIProviderSettingsStore(Defaults(gptEnabled));
        if(batch)await settings.SaveAsync(1,new("openai",false,"configured-gpt"),default);
        var runner=AnalysisRunnerFixture.Create(new Market(),marketStore,signals,[new OpenAIAnalyst(gptHttp,OpenAIAnalystTests.Options()),new DeepSeekAnalyst(deepHttp,DeepSeekAnalystTests.Options())],NullLogger<MarketAnalysisRunner>.Instance,settings:settings);
        var runs=batch?(await runner.RunAllAsync(default)).Completed:[await runner.RunProductAsync("00631L",gptEnabled?new[]{"DeepSeek"}:null,default)];
        Assert.Equal(0,gptCalls);Assert.Equal(batch?3:1,deepCalls);
        Assert.All(runs,r=>{var outcome=Assert.Single(r.Providers);Assert.Equal("deepseek",outcome.Provider);Assert.Equal("reported-deepseek",outcome.Model);Assert.Equal("configured-deep",outcome.ConfiguredModel);});
        var latest=(await signals.GetHistoryAsync(1,1,default)).OrderByDescending(x=>x.Id).First();
        Assert.Equal("reported-deepseek",latest.Model);Assert.Equal(new TokenUsage(1500,600,128),latest.Usage);
        using var input=JsonDocument.Parse(latest.InputSnapshotJson);Assert.Equal("configured-deep",input.RootElement.GetProperty("configuredModel").GetString());
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("[\"missing\"]")]
    [InlineData("[\"DeepSeek\",\"deepseek\"]")]
    [InlineData("[null]")]
    [InlineData("[\"openai\"]")]
    public async Task InvalidOrDisabledForceSelectionRejectedBeforeFetchingMarket(string json)
    {
        var market=new Market();
        await using var factory=new ApiFactory(s=>{
            s.RemoveAll<IMarketDataProvider>();s.AddSingleton<IMarketDataProvider>(market);
            s.RemoveAll<IAIProviderSettingsStore>();s.AddSingleton<IAIProviderSettingsStore>(new MemoryAIProviderSettingsStore(Defaults(false)));
        });
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        using var body=new StringContent("{\"providers\":"+json+"}",System.Text.Encoding.UTF8,"application/json");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/api/products/00631L/analysis/force",body)).StatusCode);
        Assert.Equal(0,market.Calls);
    }
    [Fact]
    public async Task SettingsChangeAppliesToNextRunWithoutRebuildAndOldActualModelRemains()
    {
        var gpt=new CountingAnalyst("openai");var deep=new CountingAnalyst("deepseek");var signals=new MemorySignalStore();var store=new MemoryMarketStore(signals);
        var settings=new MemoryAIProviderSettingsStore(Defaults());
        var runner=AnalysisRunnerFixture.Create(new Market(),store,signals,[gpt,deep],NullLogger<MarketAnalysisRunner>.Instance,settings:settings);
        Assert.Equal(2,(await runner.RunProductAsync("00631L",default)).Providers.Count);
        await settings.SaveAsync(1,new("openai",false,"changed-model"),default);
        Assert.Single((await runner.RunProductAsync("00631L",default)).Providers);
        Assert.Equal(1,gpt.Calls);Assert.Equal(2,deep.Calls);
        Assert.Equal(2,(await signals.GetHistoryAsync(1,1,default)).Count(x=>x.Model=="actual-deepseek"));
        Assert.Single(await signals.GetHistoryAsync(1,1,default),x=>x.Model=="actual-openai");
    }
    [Fact]
    public async Task AllDisabledProducesNoDecisionUsageOrExternalCall()
    {
        var forbidden=new ThrowingAnalyst();var signals=new MemorySignalStore();var store=new MemoryMarketStore(signals);
        var settings=new MemoryAIProviderSettingsStore(Defaults(false,false));
        var runner=AnalysisRunnerFixture.Create(new Market(),store,signals,[forbidden],NullLogger<MarketAnalysisRunner>.Instance,settings:settings);
        var before=(await signals.GetHistoryAsync(1,1,default)).Count;
        Assert.Empty((await runner.RunProductAsync("00631L",default)).Providers);
        Assert.Equal(0,forbidden.Calls);Assert.Empty(store.Failures);
        Assert.Equal(before,(await signals.GetHistoryAsync(1,1,default)).Count);
    }
    [Fact]
    public async Task ModelEditAppliesNextRunWithoutMutatingOptionsOrPastActualModel()
    {
        var requested=new List<string>();
        using var http=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>{
            using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            requested.Add(body.RootElement.GetProperty("model").GetString()!);
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());
        }));
        var signals=new MemorySignalStore();var store=new MemoryMarketStore(signals);var settings=new MemoryAIProviderSettingsStore(Defaults(false));
        var options=DeepSeekAnalystTests.Options();
        var runner=AnalysisRunnerFixture.Create(new Market(),store,signals,[new DeepSeekAnalyst(http,options)],NullLogger<MarketAnalysisRunner>.Instance,settings:settings);
        var first=Assert.Single((await runner.RunProductAsync("00631L",default)).Providers);
        await settings.SaveAsync(1,new("deepseek",true,"edited-model"),default);
        var second=Assert.Single((await runner.RunProductAsync("00631L",default)).Providers);
        Assert.Equal(new[]{"configured-deep","edited-model"},requested);
        Assert.Equal("configured-deepseek",options.Model);
        Assert.Equal("reported-deepseek",first.Model);Assert.Equal("reported-deepseek",second.Model);
        var records=await signals.GetHistoryAsync(1,1,default);
        using var old=JsonDocument.Parse(Assert.Single(records,x=>x.Id==first.AnalysisId).InputSnapshotJson);
        Assert.Equal("configured-deep",old.RootElement.GetProperty("configuredModel").GetString());
    }
    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"providers\":\"DeepSeek\"}")]
    public async Task MalformedOptionalBodyCannotTriggerAnalysis(string json)
    {
        var market=new Market();await using var factory=new ApiFactory(s=>{s.RemoveAll<IMarketDataProvider>();s.AddSingleton<IMarketDataProvider>(market);});
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        using var body=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync("/api/products/00631L/analysis/force",body)).StatusCode);
        Assert.Equal(0,market.Calls);
    }
    [Fact]
    public async Task VisibilityIsScopedAndIndependentAndLegacyUpdatesPreserveIt()
    {
        var forbidden = new ThrowingAnalyst();
        await using var factory = new ApiFactory(s => { s.RemoveAll<IAIAnalyst>(); s.AddSingleton<IAIAnalyst>(forbidden); });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Market-Signal", "web");
        var initial = await client.GetFromJsonAsync<AIProviderSettingView[]>("/api/ai/settings");
        Assert.All(initial!, row => Assert.True(row.Visible));
        var before = await client.GetStringAsync("/api/products/00631L/analysis/history");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/ai/settings/claude", new { enabled = false, visible = false, configuredModel = "mock-v1" })).StatusCode);
        var store = factory.Services.GetRequiredService<IAIProviderSettingsStore>();
        var hidden = Assert.Single(await store.GetAsync(1, default), x => x.Provider == "claude");
        Assert.False(hidden.Visible); Assert.False(hidden.Enabled);
        Assert.True(Assert.Single(await store.GetAsync(2, default), x => x.Provider == "claude").Visible);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/ai/settings/claude", new { enabled = true, configuredModel = "mock-v1" })).StatusCode);
        var updated = Assert.Single((await client.GetFromJsonAsync<AIProviderSettingView[]>("/api/ai/settings"))!, x => x.Provider == "claude");
        Assert.False(updated.Visible); Assert.True(updated.Enabled);
        Assert.Equal(before, await client.GetStringAsync("/api/products/00631L/analysis/history"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/ai/settings/claude", new { enabled = false, visible = true, configuredModel = "mock-v1" })).StatusCode);
        Assert.True(Assert.Single(await store.GetAsync(1, default), x => x.Provider == "claude").Visible);
        Assert.Equal(0, forbidden.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenEnabledProviderStillRunsAndVisibleDisabledProviderDoesNot(bool batch)
    {
        var signals = new MemorySignalStore(); var marketStore = new MemoryMarketStore(signals);
        var settings = new MemoryAIProviderSettingsStore(Defaults(false));
        await settings.SaveAsync(1, new("deepseek", true, "configured-deep", Visible: false), default);
        var gpt = new CountingAnalyst("openai"); var deep = new CountingAnalyst("deepseek");
        var runner = AnalysisRunnerFixture.Create(new Market(), marketStore, signals, new IAIAnalyst[] { gpt, deep }, NullLogger<MarketAnalysisRunner>.Instance, settings: settings);
        if (batch) await runner.RunAllAsync(default); else await runner.RunProductAsync("00631L", default);
        Assert.Equal(0, gpt.Calls); Assert.Equal(batch ? 3 : 1, deep.Calls);
    }
    private sealed class ThrowingAnalyst : IAIAnalyst
    {public int Calls;public string ProviderCode=>"openai";public Task<AnalystResult> AnalyzeAsync(MarketContext input,CancellationToken ct){Calls++;throw new InvalidOperationException("Settings must never execute AI.");}}
    private sealed class CountingAnalyst(string code) : IAIAnalyst
    {public int Calls;public string ProviderCode=>code;public Task<AnalystResult> AnalyzeAsync(MarketContext input,CancellationToken ct){Calls++;return Task.FromResult(new AnalystResult("actual-"+code,DemoData.MockResult("HOLD",true),"{}",Usage:new(100,50)));}}
    private sealed class Market : IMarketDataProvider
    {public int Calls;public Task<MarketSnapshot> GetSnapshotAsync(string symbol,CancellationToken ct){Calls++;return Task.FromResult(OpenAIAnalystTests.Context().Snapshot with{Symbol=symbol});}public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol,DateOnly from,DateOnly through,CancellationToken ct)=>Task.FromResult(OpenAIAnalystTests.Context().History);public Task<bool> IsTradingDayAsync(DateOnly date,CancellationToken ct)=>Task.FromResult(true);}
}
