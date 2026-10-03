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

public sealed class NewsAnalysisIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T15:00:00Z");
    private static NewsContext Context(long id=87, int minutes=5, string status="PARTIAL") => new(id,Now.AddHours(-72),Now,Now.AddMinutes(-minutes),
        "rss","deepseek","deepseek-flash","deepseek-flash",status,
        [new("event-1","MACRO","政策資訊","利率尚待確認",Now.AddHours(-5),Now.AddMinutes(-10),"POSITIVE","HIGH",80,70,"VERIFIED",null,
            [new("source-1","Official","https://example.com/policy",Now.AddMinutes(-10),"OFFICIAL")])],[],1,1,0,1,new(10,20),"news-v1");
    private static NewsEvidenceResolver Resolver(Store store, Refresh refresh, int maxAge=180) =>
        new(store,refresh,new(){MaxAgeMinutes=maxAge},new Clock(),NullLogger<NewsEvidenceResolver>.Instance);
    [Theory]
    [InlineData(5,180,"FRESH")]
    [InlineData(180,180,"FRESH")]
    [InlineData(181,180,"STALE")]
    [InlineData(60,30,"STALE")]
    [InlineData(-1,180,"UNKNOWN")]
    public async Task FreshnessIsCentralAndKeepsOldStructuredEvidence(int age,int maxAge,string expected)
    {
        var s=new Store(Context(minutes:age));var r=new Refresh(Context(88));var evidence=await Resolver(s,r,maxAge).ResolveAsync(false,default);
        Assert.Equal(expected,evidence.Freshness);Assert.Equal(87,evidence.NewsContextId);Assert.Single(evidence.Events);Assert.Equal(0,r.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshFailureUsesOldOrUnavailableEvidenceWithoutFailingAnalysis(bool hasOld)
    {
        var s=new Store(hasOld ? Context() : null);var r=new Refresh(null){Fail=true};var evidence=await Resolver(s,r).ResolveAsync(true,default);
        Assert.True(evidence.NewsRefreshFailed);Assert.Equal(hasOld ? 87L : (long?)null,evidence.NewsContextId);
        Assert.Equal(hasOld ? "FRESH" : "UNAVAILABLE",evidence.Freshness);Assert.Equal(1,r.Calls);Assert.Equal(1,s.Reads);
    }
    [Fact]
    public async Task OptionalRefreshRunsOnceBeforeMarketAndAllAnalystsShareFrozenSnapshot()
    {
        var order=new List<string>();var s=new Store(Context());var refresh=new Refresh(Context(88)){Order=order};
        var resolver=Resolver(s,refresh);var signals=new MemorySignalStore();var marketStore=new MemoryMarketStore(signals);
        var gpt=new Capture("openai",order);var deep=new Capture("deepseek",order);
        var runner=AnalysisRunnerFixture.Create(new Market(order),marketStore,signals,[gpt,deep],NullLogger<MarketAnalysisRunner>.Instance,newsEvidence:resolver);
        var result=await runner.RunProductAsync("00631L",["openai","deepseek"],true,default);
        Assert.Equal(new[]{"refresh","quote","deepseek","openai"},order);Assert.Equal(1,refresh.Calls);Assert.Equal(0,s.Reads);
        Assert.Same(gpt.Input,deep.Input);Assert.Same(gpt.Input!.NewsContext,deep.Input!.NewsContext);
        Assert.Equal(88,gpt.Input.NewsContext!.NewsContextId);
        foreach(var provider in result.Providers)
        {
            var record=Assert.Single(await signals.GetHistoryAsync(1,1,default),x=>x.Id==provider.AnalysisId);
            Assert.Equal(88,record.NewsContextId);
            using var json=JsonDocument.Parse(record.InputSnapshotJson);
            Assert.Equal(88,json.RootElement.GetProperty("newsContext").GetProperty("NewsContextId").GetInt64());
            var input=json.RootElement.GetProperty("analysisInput").GetProperty("newsContext");
            Assert.Equal("政策資訊",input.GetProperty("events")[0].GetProperty("title").GetString());
            Assert.False(input.TryGetProperty("rawSearchSnapshotJson",out _));
        }
        var views=await new SignalService(signals,new User()).GetAnalysisAsync(1,false,default);
        Assert.All(views.Where(x=>result.Providers.Any(p=>p.AnalysisId==x.Id)),x=>Assert.Equal(88,x.Context!.NewsContext!.NewsContextId));
    }
    [Fact]
    public async Task ForceReusesLatestAndBatchResolvesOnlyOnceAcrossProducts()
    {
        var store=new Store(Context());var refresh=new Refresh(Context(88));var resolver=Resolver(store,refresh);
        var signals=new MemorySignalStore();var capture=new Capture("deepseek",[]);
        var runner=AnalysisRunnerFixture.Create(new Market([]),new MemoryMarketStore(signals),signals,[capture],NullLogger<MarketAnalysisRunner>.Instance,newsEvidence:resolver);
        await runner.RunProductAsync("00631L",["deepseek"],default);Assert.Equal(1,store.Reads);Assert.Equal(0,refresh.Calls);
        await runner.RunAllAsync(default);Assert.Equal(2,store.Reads);Assert.Equal(0,refresh.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrFailedStorageStillAllowsTargetAnalysis(bool failStorage)
    {
        var resolver=Resolver(new(null){Fail=failStorage},new(null));var signals=new MemorySignalStore();var deep=new Capture("deepseek",[]);
        var runner=AnalysisRunnerFixture.Create(new Market([]),new MemoryMarketStore(signals),signals,[deep],NullLogger<MarketAnalysisRunner>.Instance,newsEvidence:resolver);
        var result=await runner.RunProductAsync("00631L",["deepseek"],default);
        Assert.Equal("COMPLETED",Assert.Single(result.Providers).Status);Assert.Equal("UNAVAILABLE",deep.Input!.NewsContext!.Status);
    }
    [Fact]
    public async Task LiveAdaptersReceiveIdenticalNewsJsonAndEvidenceOnlyRulesWithoutDbOrSearch()
    {
        var evidence=await Resolver(new(Context()),new(null)).ResolveAsync(false,default);
        var context=OpenAIAnalystTests.Context() with {NewsContext=evidence};string? open=null,deep=null,instructions=null;
        using var oh=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>{
            using var json=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            open=json.RootElement.GetProperty("input").GetString();instructions=json.RootElement.GetProperty("instructions").GetString();
            return OpenAIAnalystTests.Json(OpenAIAnalystTests.Response());}));
        using var dh=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>{
            using var json=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            deep=json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            Assert.StartsWith(instructions!,json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());}));
        var gpt=await new OpenAIAnalyst(oh,OpenAIAnalystTests.Options()).AnalyzeAsync(context,default);
        var ds=await new DeepSeekAnalyst(dh,DeepSeekAnalystTests.Options()).AnalyzeAsync(context,default);
        Assert.Equal(open,deep);Assert.Contains("\"newsContextId\":87",open);Assert.Contains("POSITIVE never automatically means ADD",instructions);
        Assert.Contains("Never search",instructions);Assert.Equal("HOLD",gpt.Analysis.Action);Assert.Equal("HOLD",ds.Analysis.Action);
    }
    [Fact]
    public async Task ForceApiForwardsRefreshOptionAndLeavesItFalseByDefault()
    {
        var runner=new ApiRunner();await using var factory=new ApiFactory(s=>{s.RemoveAll<IMarketAnalysisRunner>();s.AddSingleton<IMarketAnalysisRunner>(runner);});
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/products/00631L/analysis/force",new {providers=new[]{"deepseek"},refreshNewsBeforeAnalysis=true})).StatusCode);
        Assert.True(runner.Refresh);Assert.Equal("deepseek",Assert.Single(runner.Providers!));
        await client.PostAsJsonAsync("/api/products/00631L/analysis/force",new {});Assert.False(runner.Refresh);
    }
    [Fact]
    public async Task PersistedNewsPreferenceIsSafeUserScopedAndOnlyDefaultsManualForce()
    {
        var runner=new ApiRunner();await using var factory=new ApiFactory(s=>{s.RemoveAll<IMarketAnalysisRunner>();s.AddSingleton<IMarketAnalysisRunner>(runner);});
        using var client=factory.CreateClient();
        var initial=await client.GetFromJsonAsync<AnalysisExecutionSettings>("/api/ai/analysis-settings");Assert.False(initial!.RefreshNewsBeforeAnalysis);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("/api/ai/analysis-settings",new {refreshNewsBeforeAnalysis=true})).StatusCode);
        client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/ai/analysis-settings",new {})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/ai/analysis-settings",new {refreshNewsBeforeAnalysis=true})).StatusCode);
        Assert.True((await client.GetFromJsonAsync<AnalysisExecutionSettings>("/api/ai/analysis-settings"))!.RefreshNewsBeforeAnalysis);
        Assert.False(runner.Refresh); // Saving preferences never runs analysis.
        var store=factory.Services.GetRequiredService<IAnalysisExecutionSettingsStore>();Assert.False((await store.GetAsync(2,default)).RefreshNewsBeforeAnalysis);
        await client.PostAsJsonAsync("/api/products/00631L/analysis/force",new {});Assert.True(runner.Refresh);
        await client.PostAsJsonAsync("/api/products/00631L/analysis/force",new {refreshNewsBeforeAnalysis=false});Assert.False(runner.Refresh);
        await client.PutAsJsonAsync("/api/ai/analysis-settings",new {refreshNewsBeforeAnalysis=false});
        await client.PostAsJsonAsync("/api/products/00631L/analysis/force",new {});Assert.False(runner.Refresh);
    }
    [Fact]
    public async Task LatestValidSkipsInvalidStoredContexts()
    {
        var store=new MemoryNewsContextStore();var audit=new NewsRefreshAudit("[]","{}","prompt","{}","{}");
        var valid=await store.AppendAsync(Context(),audit,default);await store.AppendAsync(Context(88,status:"FAILED"),audit,default);
        Assert.Equal(valid.Id,(await store.GetLatestValidAsync(default))!.Id);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class User : ICurrentUser { public long UserId=>1; }
    private sealed class Store(NewsContext? context) : INewsContextStore
    {
        public int Reads;public bool Fail;
        public Task<NewsContext?> GetLatestAsync(CancellationToken ct){Reads++;if(Fail)throw new IOException("store down");return Task.FromResult(context);}
        public Task<NewsContext?> GetAsync(long id,CancellationToken ct)=>Task.FromResult(context);
        public Task<NewsContext> AppendAsync(NewsContext c,NewsRefreshAudit a,CancellationToken ct)=>throw new NotSupportedException();
        public Task AppendFailureAsync(NewsRefreshFailure f,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Refresh(NewsContext? context) : INewsContextRefresher
    {
        public int Calls;public bool Fail;public List<string>? Order;
        public Task<NewsContext> RefreshAsync(CancellationToken ct){Calls++;Order?.Add("refresh");if(Fail)throw new IOException("refresh failed");return Task.FromResult(context!);}
    }
    private sealed class Market(List<string> order) : IMarketDataProvider
    {
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol,CancellationToken ct){order.Add("quote");return Task.FromResult(OpenAIAnalystTests.Context().Snapshot with {Symbol=symbol});}
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol,DateOnly f,DateOnly t,CancellationToken ct)=>Task.FromResult(OpenAIAnalystTests.Context().History);
        public Task<bool> IsTradingDayAsync(DateOnly d,CancellationToken ct)=>Task.FromResult(true);
    }
    private sealed class Capture(string code,List<string> order) : IAIAnalyst
    {
        public string ProviderCode=>code;public MarketContext? Input;
        public Task<AnalystResult> AnalyzeAsync(MarketContext c,CancellationToken ct){order.Add(code);Input=c;return Task.FromResult(new AnalystResult("captured-"+code,DemoData.MockResult("HOLD",true),"{}"));}
    }
    private sealed class ApiRunner : IMarketAnalysisRunner
    {
        public bool Refresh;public IReadOnlyList<string>? Providers;
        public Task<ProductRunResult> RunProductAsync(string s,CancellationToken ct,DateOnly? d=null)=>Task.FromResult(new ProductRunResult(s,1,[]));
        public Task<ProductRunResult> RunProductAsync(string s,IReadOnlyList<string>? providers,bool refresh,CancellationToken ct){Refresh=refresh;Providers=providers;return RunProductAsync(s,ct);}
        public Task<BatchRunResult> RunAllAsync(CancellationToken ct,DateOnly? d=null)=>Task.FromResult(new BatchRunResult([],[]));
    }
}
