using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;

public sealed class AnalysisRunnerRefactorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T15:00:00Z");
    [Fact]
    public async Task CompleteInputSnapshotMatchesPreRefactorBaselineByteForByte()
    {
        var signals=new FixedSignals();var store=new MemoryMarketStore(signals.Inner);
        var refs=new MemoryMarketReferenceStore();var instrument=await refs.SaveInstrumentAsync(1,null,"^TWII","TAIEX","TW",default);
        await refs.SaveReferenceAsync(1,1,null,instrument.Id,"BROAD_MARKET",default);
        var analyst=new Capture("deepseek");var news=new News();
        var runner=AnalysisRunnerFixture.Create(new Market(),store,signals,[analyst],NullLogger<MarketAnalysisRunner>.Instance,refs,
            new MemoryAIProviderSettingsStore([new("deepseek",true,"configured-deep")]),new MemoryPromptStore(),new Tw(),news);
        var result=await runner.RunProductAsync("00631L",["deepseek"],default);
        var record=Assert.Single(await signals.Inner.GetHistoryAsync(1,1,default),x=>x.Id==Assert.Single(result.Providers).AnalysisId);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","analysis-input-before-refactor.json")),record.InputSnapshotJson);
        Assert.Equal(87,record.NewsContextId);Assert.Equal(1,news.Calls);
    }
    [Fact]
    public async Task BuilderReusesScopeCachesAndNeverResolvesNewsOrExecutesProviders()
    {
        var signals=new FixedSignals();var store=new MemoryMarketStore(signals.Inner);var refs=new MemoryMarketReferenceStore();
        var instrument=await refs.SaveInstrumentAsync(1,null,"^TWII","TAIEX","TW",default);
        await refs.SaveReferenceAsync(1,1,null,instrument.Id,"BROAD_MARKET",default);
        var market=new Market();var tw=new Tw();var builder=new AnalysisContextBuilder(market,store,signals,NullLogger<AnalysisContextBuilder>.Instance,refs,twMarket:tw);
        var scope=builder.CreateScope();var product=DemoData.Products.First();var enabled=await signals.GetProvidersAsync(default);
        var news=await new News().ResolveAsync(false,default);
        var evidence=await builder.FetchMarketAsync(product,null,scope,default);
        var first=await builder.BuildAsync(product,1,enabled,evidence,news,scope,default);
        var again=await builder.BuildAsync(product,1,enabled,evidence,news,scope,default);
        Assert.Equal(1,market.TargetQuotes);Assert.Equal(1,market.ReferenceQuotes);Assert.Equal(1,market.ReferenceHistories);Assert.Equal(1,tw.Calls);
        Assert.Same(news,first.Context.NewsContext);Assert.Same(news,again.Context.NewsContext);
        Assert.Equal(first.SharedInput.GetRawText(),again.SharedInput.GetRawText());
        Assert.Same(first.Context.Snapshot,again.Context.Snapshot);Assert.Same(first.Context.TwMarketContext,again.Context.TwMarketContext);
        await builder.FetchMarketAsync(product,null,scope,default);Assert.Equal(1,tw.Calls);
        var nextScope=builder.CreateScope();var nextEvidence=await builder.FetchMarketAsync(product,null,nextScope,default);
        await builder.BuildAsync(product,1,enabled,nextEvidence,news,nextScope,default);
        Assert.Equal(2,market.ReferenceQuotes);Assert.Equal(2,market.ReferenceHistories);Assert.Equal(2,tw.Calls);
    }
    [Fact]
    public async Task ExecutionIsolatesFailurePreservesUsageModelAndPreviouslyFrozenDecisions()
    {
        var signals=new FixedSignals();var memory=new MemoryMarketStore(signals.Inner);var store=new WriteOnlyStore(memory);
        var builder=new AnalysisContextBuilder(new Market(),memory,signals,NullLogger<AnalysisContextBuilder>.Instance);
        var product=DemoData.Products.First();var providers=await signals.GetProvidersAsync(default);var scope=builder.CreateScope();
        var prepared=await builder.BuildAsync(product,1,providers,await builder.FetchMarketAsync(product,null,scope,default),await new News().ResolveAsync(false,default),scope,default);
        var originalInput=prepared.SharedInput.GetRawText();var priors=prepared.Context.PreviousDecisions;
        var failed=new ExecutionAnalyst("deepseek",true);var succeeded=new ExecutionAnalyst("openai",false);
        var service=new AIProviderExecutionService(store,[failed,succeeded],NullLogger<AIProviderExecutionService>.Instance);
        var plan=new[]{providers.Single(x=>x.Code=="deepseek"),providers.Single(x=>x.Code=="openai")};
        var settings=new AIProviderSetting[]{new("deepseek",true,"configured-ds"),new("openai",true,"configured-gpt")};
        var outcomes=await service.ExecuteAsync(1,prepared,plan,settings,service.GetAdapters(),default);
        Assert.Equal("FAILED",outcomes[0].Status);Assert.Equal("configured-ds",outcomes[0].ConfiguredModel);Assert.Equal(new TokenUsage(9,3,1),outcomes[0].Usage);
        Assert.Equal("COMPLETED",outcomes[1].Status);Assert.Equal("actual-openai",outcomes[1].Model);Assert.Equal("configured-gpt",outcomes[1].ConfiguredModel);
        Assert.Same(prepared.Context,failed.Seen);Assert.Same(failed.Seen,succeeded.Seen);Assert.Same(priors,succeeded.Seen!.PreviousDecisions);
        Assert.Single(priors);Assert.Equal("previous-gpt",priors[0].Model);Assert.Equal(originalInput,prepared.SharedInput.GetRawText());
        var failure=Assert.Single(memory.Failures);Assert.Equal("configured-ds",failure.Model);Assert.Equal(new TokenUsage(9,3,1),failure.Usage);
        var record=Assert.Single(await signals.Inner.GetHistoryAsync(1,1,default),x=>x.Id==outcomes[1].AnalysisId);Assert.Equal(87,record.NewsContextId);
        using var json=JsonDocument.Parse(record.InputSnapshotJson);Assert.Equal(JsonSerializer.Serialize(prepared.SharedInput),json.RootElement.GetProperty("analysisInput").GetRawText());
    }
    [Fact]
    public async Task MissingAdapterAndTimeoutHaveSameOutcomeAndDoNotPreventNextProvider()
    {
        var context=OpenAIAnalystTests.Context() with {Prompt=AnalysisProtocol.CapturePrompt(new(0,"investment-analysis-v1",AnalysisProtocol.CommonInstructions,DateTime.MinValue),true)};var prepared=new PreparedAnalysisContext(context,JsonSerializer.Deserialize<JsonElement>(AnalysisProtocol.Input(context)));
        var signals=new MemorySignalStore();var store=new MemoryMarketStore(signals);var timeout=new TimeoutAnalyst();var success=new ExecutionAnalyst("openai",false);
        var service=new AIProviderExecutionService(store,[timeout,success],NullLogger<AIProviderExecutionService>.Instance);
        var results=await service.ExecuteAsync(1,prepared,[DemoData.Providers.Single(x=>x.Code=="claude"),DemoData.Providers.Single(x=>x.Code=="deepseek"),DemoData.Providers.Single(x=>x.Code=="openai")],null,service.GetAdapters(),default);
        Assert.Equal(new[]{"UNAVAILABLE","FAILED","COMPLETED"},results.Select(x=>x.Status));Assert.Equal("AI timeout.",results[1].Error);Assert.Single(store.Failures);
    }
    private sealed class TimeoutAnalyst : IAIAnalyst
    {
        public string ProviderCode=>"deepseek";public TimeSpan Timeout=>TimeSpan.FromMilliseconds(10);
        public async Task<AnalystResult> AnalyzeAsync(MarketContext c,CancellationToken ct){await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan,ct);throw new InvalidOperationException();}
    }
    private sealed class ExecutionAnalyst(string code,bool fail) : IAIAnalyst
    {
        public string ProviderCode=>code;public string Model {get;private set;}="default";public MarketContext? Seen;
        public IAIAnalyst WithModel(string model){Model=model;return this;}
        public Task<AnalystResult> AnalyzeAsync(MarketContext c,CancellationToken ct){Seen=c;if(fail)throw new AIProviderException("provider failed",new(9,3,1));return Task.FromResult(new AnalystResult("actual-"+code,DemoData.MockResult("HOLD",true),"{}",Usage:new(10,5)));}
    }
    // If the execution component accidentally orchestrates data reads, this boundary test fails.
    private sealed class WriteOnlyStore(MemoryMarketStore inner) : IMarketStore
    {
        public Task<StoredMarketSnapshot?> GetLatestSnapshotAsync(long p,CancellationToken c)=>throw new InvalidOperationException("No reads during execution");
        public Task<StoredMarketSnapshot> SaveSnapshotAsync(long p,MarketSnapshot s,CancellationToken c)=>throw new InvalidOperationException("No market fetching during execution");
        public Task<TradingDay?> GetTradingDayAsync(DateOnly d,string m,CancellationToken c)=>throw new InvalidOperationException("No schedule during execution");
        public Task SaveTradingDayAsync(TradingDay d,CancellationToken c)=>throw new InvalidOperationException();
        public Task<IReadOnlyList<Product>> GetActiveTrackedProductsAsync(string m,CancellationToken c)=>throw new InvalidOperationException();
        public Task<IReadOnlyList<long>> GetTrackingUserIdsAsync(long p,CancellationToken c)=>throw new InvalidOperationException();
        public Task<AnalysisRecord> SaveAnalysisAsync(AnalysisRecord r,CancellationToken c)=>inner.SaveAnalysisAsync(r,c);
        public Task SaveProviderFailureAsync(ProviderFailure f,CancellationToken c)=>inner.SaveProviderFailureAsync(f,c);
    }
    private sealed class FixedSignals : ISignalStore
    {
        public MemorySignalStore Inner {get;}=new();
        public Task<User?> GetUserAsync(long u,CancellationToken c)=>Inner.GetUserAsync(u,c);
        public Task<IReadOnlyList<AIProvider>> GetProvidersAsync(CancellationToken c)=>Inner.GetProvidersAsync(c);
        public Task<IReadOnlyList<Product>> SearchProductsAsync(string q,CancellationToken c)=>Inner.SearchProductsAsync(q,c);
        public Task<Product?> GetProductAsync(string s,string m,CancellationToken c)=>Inner.GetProductAsync(s,m,c);
        public Task<IReadOnlyList<Product>> GetWatchlistAsync(long u,CancellationToken c)=>Inner.GetWatchlistAsync(u,c);
        public Task AddWatchAsync(long u,long p,CancellationToken c)=>Inner.AddWatchAsync(u,p,c);
        public Task RemoveWatchAsync(long u,long p,CancellationToken c)=>Inner.RemoveWatchAsync(u,p,c);
        public Task<UserPosition?> GetPositionAsync(long u,long p,CancellationToken c)=>Task.FromResult<UserPosition?>(new(u,p,3,34,Now.UtcDateTime));
        public Task SavePositionAsync(UserPosition p,CancellationToken c)=>Inner.SavePositionAsync(p,c);
        public Task<IReadOnlyList<AnalysisRecord>> GetHistoryAsync(long u,long? p,CancellationToken c)=>Task.FromResult<IReadOnlyList<AnalysisRecord>>(
            [new(5,u,p??1,DemoData.Providers.Single(x=>x.Code=="openai").Id,"previous-gpt",DemoData.MockResult("HOLD",true),"{}","{}",Now.AddDays(-1).UtcDateTime)]);
        public Task<bool> IsHealthyAsync(CancellationToken c)=>Inner.IsHealthyAsync(c);
    }
    private sealed class Market : IMarketDataProvider
    {
        public int TargetQuotes,ReferenceQuotes,ReferenceHistories;
        public Task<MarketSnapshot> GetSnapshotAsync(string s,CancellationToken c){TargetQuotes++;return Task.FromResult(new MarketSnapshot(s,40,39,41,38,39,1000,Now,Now));}
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string s,DateOnly f,DateOnly t,CancellationToken c)=>Task.FromResult<IReadOnlyList<HistoricalPrice>>([new(new(2026,10,2),39,40,38,39,1000),new(new(2026,10,3),39,41,38,40,1000)]);
        public Task<MarketSnapshot> GetReferenceSnapshotAsync(string s,CancellationToken c){ReferenceQuotes++;return Task.FromResult(new MarketSnapshot(s,40,39,41,38,39,1000,Now,Now));}
        public Task<ReferenceHistoryData> GetReferenceHistoryAsync(string s,DateOnly f,DateOnly t,CancellationToken c){ReferenceHistories++;return Task.FromResult(new ReferenceHistoryData([new(new(2026,10,2),39,40,38,39,1000),new(new(2026,10,3),39,41,38,40,1000)],new("FIXTURE",s,false,"OHLCV",null,f,t,Now,[])));}
        public Task<bool> IsTradingDayAsync(DateOnly d,CancellationToken c)=>Task.FromResult(true);
    }
    private sealed class Tw : ITwMarketContextProvider
    {
        public int Calls;
        public Task<TwMarketContext> GetAsync(string s,DateOnly d,CancellationToken c)
        {
            Calls++;var e=new TwEvidence("FIXTURE",d,Now,"AVAILABLE","EOD");var flow=new TwInstitutionSeries(e,"shares",100,500,2000);
            return Task.FromResult(new TwMarketContext(new(e,"NTD",100,90,80,11,25),new(e,"stocks",400,300,20,5,3),
                new(new(e,"NTD_thousands",100,1,5,20),new(e,"TWSE_trading_units",10,1,2,3),new(e,"TWSE_trading_units",3,0,1,2)),
                new(flow,flow,flow,flow,flow,flow)));
        }
    }
    private sealed class News : INewsEvidenceResolver
    {
        public int Calls;
        public Task<NewsEvidenceContext> ResolveAsync(bool refresh,CancellationToken c)
        {
            Calls++;return Task.FromResult(new NewsEvidenceContext(87,Now,Now.AddHours(-72),Now,"PARTIAL","FRESH",Now,180,false,null,
                [new("one","MACRO","Policy","Evidence",null,Now,"NEUTRAL","HIGH",80,70,"UNKNOWN",null,[new("source","Official","https://example.com/news",Now,"OFFICIAL")])]));
        }
    }
    private sealed class Capture(string code) : IAIAnalyst
    {
        public string ProviderCode=>code;public string Model=>"configured-deep";
        public Task<AnalystResult> AnalyzeAsync(MarketContext c,CancellationToken ct)=>Task.FromResult(new AnalystResult("actual-deep",DemoData.MockResult("HOLD",true),"{}","fixed instructions",new(100,50,10),null));
    }
}
