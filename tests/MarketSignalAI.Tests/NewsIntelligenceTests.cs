using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;

public sealed class NewsIntelligenceTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-03T13:55:00Z");
    internal static NewsSearchResult Article(string id="one",DateTimeOffset? time=null,string topic="MACRO")=>new(id,topic,"Policy update","https://example.com/"+id,"Official",time ?? Now.AddMinutes(-20),"Policy update at 2026-10-03T12:00:00Z. Rates unchanged.","OFFICIAL","VERIFIED");
    internal static NewsEventDraft Event(params string[] ids)=>new("MACRO","政策更新","政策資訊，方向尚不確定。",null,"NEUTRAL","HIGH",90,70,"UNKNOWN",null,ids.Length==0 ? ["one"] : ids);
    internal static NewsCollection Collection(IReadOnlyList<NewsEventDraft> events)=>new(events,"deepseek-flash",new(123,45,20),"news-intelligence-v1","test prompt","{}","test response");
    private static NewsContextRefresher Service(Search search,Collector collector,MemoryNewsContextStore store,Clock clock,NewsRefreshGate? gate=null)=>new(search,collector,store,gate ?? new(),clock,new(),NullLogger<NewsContextRefresher>.Instance);

    [Fact]
    public async Task RollingWindowUsesExecutionNowAndEachRefreshAppendsWithFreshSearch()
    {
        var clock=new Clock(Now);var search=new Search([Article()]);var collector=new Collector([Event()]);var store=new MemoryNewsContextStore();var service=Service(search,collector,store,clock);
        var first=await service.RefreshAsync(default);
        Assert.Equal(Now,first.WindowEnd);Assert.Equal(Now.AddHours(-72),first.WindowStart);Assert.Equal(TimeSpan.FromHours(72),first.WindowEnd-first.WindowStart);
        var original=(await store.GetAsync(first.Id,default))!;
        clock.Now=Now.AddMinutes(85);search.Rows=[Article(),Article("latest",Now.AddMinutes(72))];collector.Events=[Event("one","latest")];
        var second=await service.RefreshAsync(default);
        Assert.Equal(2,search.Calls);Assert.Equal(2,second.Id);Assert.Equal(clock.Now,second.WindowEnd);Assert.Equal(2,second.Events[0].Sources.Count);
        Assert.Equal(first,await store.GetAsync(first.Id,default));Assert.Equal(original,await store.GetAsync(first.Id,default));
        Assert.Equal(second,await store.GetLatestAsync(default));Assert.Contains("windowStart",store.Audits[second.Id].NormalizedInputJson);
        Assert.Contains("latest",store.Audits[second.Id].RawSearchSnapshotJson);
    }

    [Fact]
    public async Task BackendFiltersOldFutureAndUnknownTimeAndDeduplicatesSameUrl()
    {
        var same=Article("copy") with {Url="https://example.com/one"};
        var search=new Search([Article(),same,Article("old",Now.AddHours(-72).AddTicks(-1)),Article("future",Now.AddTicks(1)),Article("unknown") with {PublishedAt=null,TimeQuality="UNKNOWN"}]);
        var collector=new Collector([Event()]);var result=await Service(search,collector,new(),new(Now)).RefreshAsync(default);
        Assert.Single(collector.Input!.Results);Assert.Equal(1,result.UnknownTimeCount);Assert.Equal(5,result.SearchResultCount);Assert.Equal(1,result.FilteredResultCount);
    }

    [Theory]
    [InlineData(-72)]
    [InlineData(0)]
    public async Task WindowBoundaryIsInclusive(int hours)
    {
        var collector=new Collector([Event()]);await Service(new([Article(time:Now.AddHours(hours))]),collector,new(),new(Now)).RefreshAsync(default);
        Assert.Single(collector.Input!.Results);
    }

    [Fact]
    public void EventTimeIsIndependentFromPublicationTimeAndOldEventIsRecap()
    {
        var article=Article() with {Snippet="The original event occurred at 2026-09-28T12:00:00Z."};
        var draft=Event() with {EventTime=DateTimeOffset.Parse("2026-09-28T12:00:00Z"),TimeQuality="RECAP",EventTimeEvidence="2026-09-28T12:00:00Z"};
        var saved=Assert.Single(NewsContextRefresher.Validate([draft],new(Now.AddHours(-72),Now,[article])));
        Assert.Equal(draft.EventTime,saved.EventTime);Assert.Equal(article.PublishedAt,saved.PublishedAt);Assert.Equal("RECAP",saved.TimeQuality);
    }

    [Fact]
    public void DateOnlyOldEventIsRecapWithoutInventingMidnight()
    {
        var article=Article() with {Snippet="Review of the event dated 2026-09-28."};
        var result=Assert.Single(NewsContextRefresher.Validate([Event() with {TimeQuality="RECAP",EventTimeEvidence="2026-09-28"}],new(Now.AddHours(-72),Now,[article])));
        Assert.Null(result.EventTime);Assert.Equal("RECAP",result.TimeQuality);
    }

    [Fact]
    public void FullTimestampCannotDifferFromQuotedEvidence()
    {
        var eventWithWrongTime=Event() with {EventTime=Now,TimeQuality="SUPPORTED",EventTimeEvidence="2026-10-03T12:00:00Z"};
        var result=Assert.Single(NewsContextRefresher.Validate([eventWithWrongTime],new(Now.AddHours(-72),Now,[Article()])));Assert.Null(result.EventTime);Assert.Equal("UNKNOWN",result.TimeQuality);Assert.NotNull(result.TimeQualityReason);
    }

    [Theory]
    [InlineData("ADD")]
    [InlineData("HOLD")]
    [InlineData("REDUCE")]
    [InlineData("EXIT")]
    public void CollectorCannotOutputTradingAction(string action)=>Assert.Throws<NewsPipelineException>(()=>NewsContextRefresher.Validate([Event() with {Summary=action}],new(Now.AddHours(-72),Now,[Article()])));

    [Fact]
    public void CannotInventSourceOrReuseArticleAsIndependentEvents()
    {
        var input=new NewsIntelligenceInput(Now.AddHours(-72),Now,[Article()]);
        Assert.Throws<NewsPipelineException>(()=>NewsContextRefresher.Validate([Event("invented")],input));
        Assert.Throws<NewsPipelineException>(()=>NewsContextRefresher.Validate([Event(),Event() with {Title="Other event"}],input));
        Assert.Null(Assert.Single(NewsContextRefresher.Validate([Event() with {EventTime=Now,TimeQuality="SUPPORTED",EventTimeEvidence="not supplied"}],input)).EventTime);
    }

    [Theory]
    [InlineData("SEARCH")]
    [InlineData("FLASH")]
    public async Task FailureDoesNotCreateFakeContextAndPreservesExistingLatest(string stage)
    {
        var store=new MemoryNewsContextStore();var search=new Search([Article()]);var collector=new Collector([Event()]);var service=Service(search,collector,store,new(Now));
        var existing=await service.RefreshAsync(default);
        if(stage=="SEARCH")search.Fail=true;else collector.Fail=true;
        await Assert.ThrowsAsync<NewsPipelineException>(()=>service.RefreshAsync(default));Assert.Equal(existing,await store.GetLatestAsync(default));Assert.Single(store.Failures);
    }

    [Fact]
    public async Task ConcurrentRefreshIsRejectedBeforeSearchOrPaidCall()
    {
        var gate=new NewsRefreshGate();await gate.Semaphore.WaitAsync();var search=new Search([Article()]);
        try{await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(search,new([Event()]),new(),new(Now),gate).RefreshAsync(default));Assert.Equal(0,search.Calls);}
        finally{gate.Semaphore.Release();}
    }

    [Theory]
    [InlineData("Fri, 02 Oct 2026 20:45:00 GMT",true)]
    [InlineData("2026-10-03T19:30:00+08:00",true)]
    [InlineData("2026-10-03",false)]
    [InlineData("2026-10-03T12:00:00",false)]
    [InlineData("unknown",false)]
    public void RssTimestampRequiresExplicitZoneAndPreservesUnknown(string date,bool known)
    {
        var xml=new XElement("item",new XElement("title","Policy"),new XElement("link","https://example.com/news"),new XElement("pubDate",date));
        var row=RssNewsSearchProvider.Parse(xml,new("Official","MACRO","https://example.com/feed","OFFICIAL"))!;
        Assert.Equal(known,row.PublishedAt.HasValue);Assert.Equal(known ? "VERIFIED" : "UNKNOWN",row.TimeQuality);
        if(known)Assert.Equal(TimeSpan.Zero,row.PublishedAt!.Value.Offset);
    }

    [Fact]
    public void AtomUpdatedIsNotAssumedToBePublishedAndUnsafeLinksAreRejected()
    {
        var xml=XElement.Parse("<entry><title>Update</title><link href='https://example.com/news'/><updated>2026-10-03T12:00:00Z</updated></entry>");
        Assert.Null(RssNewsSearchProvider.Parse(xml,new("Official","MACRO","https://example.com/feed","OFFICIAL"))!.PublishedAt);
        Assert.Null(RssNewsSearchProvider.Parse(XElement.Parse("<item><title>Bad</title><link>javascript:alert(1)</link></item>"),new("Official","MACRO","https://example.com/feed","OFFICIAL")));
    }

    [Fact]
    public async Task SingleRssFailureIsVisibleAndOtherSourcesStillWork()
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(req.RequestUri!.Host=="failed.example.com" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<rss><channel><item><title>Policy</title><link>https://example.com/one</link><pubDate>Fri, 02 Oct 2026 20:45:00 GMT</pubDate></item></channel></rss>")})));
        var options=new NewsOptions{Feeds=[new("Good","MACRO","https://example.com/rss","OFFICIAL"),new("Failed","TECH","https://failed.example.com/rss","OFFICIAL")]};
        var batch=await new RssNewsSearchProvider(http,options,NullLogger<RssNewsSearchProvider>.Instance).SearchAsync(new(Now.AddHours(-72),Now,["MACRO","TECH"]),default);
        Assert.Single(batch.Results);Assert.Contains(batch.Coverage,x=>x.Status=="UNAVAILABLE" && x.Limitation=="HTTP 503");
    }

    [Fact]
    public async Task AllRssFailuresCannotReturnFakeEmptySuccess()
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await Assert.ThrowsAsync<NewsPipelineException>(()=>new RssNewsSearchProvider(http,new(){Feeds=[new("Failed","MACRO","https://example.com/rss","OFFICIAL")]},NullLogger<RssNewsSearchProvider>.Instance).SearchAsync(new(Now.AddHours(-72),Now,["MACRO"]),default));
    }

    [Fact]
    public async Task RssCannotResolveExternalEntityOrLocalFile()
    {
        using var http=new HttpClient(new OpenAIAnalystTests.Handler((req,ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<!DOCTYPE rss [<!ENTITY payload SYSTEM 'file:///secret'>]><rss><channel><item><title>&payload;</title></item></channel></rss>")})));
        await Assert.ThrowsAsync<NewsPipelineException>(()=>new RssNewsSearchProvider(http,new(){Feeds=[new("Failed","MACRO","https://example.com/rss","OFFICIAL")]},NullLogger<RssNewsSearchProvider>.Instance).SearchAsync(new(Now.AddHours(-72),Now,["MACRO"]),default));
    }

    [Fact]
    public async Task ApiRefreshLatestAndHistoryUseAppendOnlyStore()
    {
        var clock=new Clock(Now);var search=new Search([Article()]);var collector=new Collector([Event()]);
        await using var factory=new ApiFactory(s=>{s.RemoveAll<INewsSearchProvider>();s.RemoveAll<INewsIntelligenceCollector>();s.RemoveAll<TimeProvider>();s.AddSingleton<INewsSearchProvider>(search);s.AddSingleton<INewsIntelligenceCollector>(collector);s.AddSingleton<TimeProvider>(clock);});
        using var client=factory.CreateClient();Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/news-context/latest")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("/api/news-context/refresh",null)).StatusCode);client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        var first=(await (await client.PostAsync("/api/news-context/refresh",null)).Content.ReadFromJsonAsync<NewsContext>())!;clock.Now=Now.AddMinutes(1);
        var second=(await (await client.PostAsync("/api/news-context/refresh",null)).Content.ReadFromJsonAsync<NewsContext>())!;
        Assert.Equal(second.Id,(await client.GetFromJsonAsync<NewsContext>("/api/news-context/latest"))!.Id);
        Assert.Equal(first.WindowEnd,(await client.GetFromJsonAsync<NewsContext>("/api/news-context/"+first.Id))!.WindowEnd);
        Assert.Equal(2,search.Calls);Assert.DoesNotContain("RawSearchSnapshotJson",await client.GetStringAsync("/api/news-context/latest"));
        collector.Fail=true;Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsync("/api/news-context/refresh",null)).StatusCode);
        Assert.Equal(second.Id,(await client.GetFromJsonAsync<NewsContext>("/api/news-context/latest"))!.Id);
    }
    private sealed class Clock(DateTimeOffset now):TimeProvider {public DateTimeOffset Now=now;public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Search(NewsSearchResult[] rows):INewsSearchProvider
    {
        public NewsSearchResult[] Rows=rows;public int Calls;public bool Fail;
        public Task<NewsSearchBatch> SearchAsync(NewsSearchQuery query,CancellationToken ct){Calls++;if(Fail)throw new Exception("test failure");return Task.FromResult(new NewsSearchBatch("TEST",Rows,[new("Official","MACRO","AVAILABLE",Rows.Length,null)]));}
    }
    private sealed class Collector(NewsEventDraft[] events):INewsIntelligenceCollector
    {
        public NewsEventDraft[] Events=events;public bool Fail;public NewsIntelligenceInput? Input;public string ConfiguredModel=>"deepseek-flash";
        public Task<NewsCollection> CollectAsync(NewsIntelligenceInput input,CancellationToken ct){Input=input;if(Fail)throw new NewsPipelineException("FLASH","測試失敗");return Task.FromResult(Collection(Events));}
    }
}
