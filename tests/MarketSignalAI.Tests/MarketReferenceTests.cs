using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace MarketSignalAI.Tests;

public sealed class MarketReferenceTests
{
    [Fact]
    public async Task CrudAllowsMultipleSectorReferencesAndIsolatesOwners()
    {
        var store=new MemoryMarketReferenceStore();
        var first=await store.SaveInstrumentAsync(1,null,"^TWII","TAIEX","TW",default);
        var second=await store.SaveInstrumentAsync(1,null,"^SOX","SOX","US",default);
        var one=await store.SaveReferenceAsync(1,1,null,first.Id,"SECTOR",default);
        var two=await store.SaveReferenceAsync(1,1,null,second.Id,"SECTOR",default);
        Assert.Equal(2,(await store.GetReferencesAsync(1,1,default)).Count);
        Assert.Empty(await store.GetReferencesAsync(2,1,default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>store.SaveReferenceAsync(2,1,null,first.Id,"SECTOR",default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>store.DeleteReferenceAsync(2,1,one.Id,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveReferenceAsync(1,1,null,first.Id,"SECTOR",default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.DeleteInstrumentAsync(1,first.Id,default));
        await store.SaveInstrumentAsync(1,first.Id,"^TWII","Renamed TAIEX","TW",default);
        await store.SaveReferenceAsync(1,1,one.Id,first.Id,"BROAD_MARKET",default);
        Assert.Contains(await store.GetReferencesAsync(1,1,default),x=>x.ReferenceType=="BROAD_MARKET" && x.Instrument.Name=="Renamed TAIEX");
        await store.DeleteReferenceAsync(1,1,one.Id,default);await store.DeleteReferenceAsync(1,1,two.Id,default);
        await store.DeleteInstrumentAsync(1,first.Id,default);await store.DeleteInstrumentAsync(1,second.Id,default);
        Assert.Empty(await store.GetInstrumentsAsync(1,default));
    }
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("^TWII?secret=x")]
    [InlineData("../00631L")]
    [InlineData(" ")]
    [InlineData("^twii")]
    public void UnsafeYahooTickerIsRejected(string ticker)=>Assert.Throws<ArgumentException>(()=>MarketReferenceValidation.YahooSymbol(ticker));

    [Fact]
    public async Task HttpCrudValidatesAndPreservesExistingAnalysisApi()
    {
        await using var factory=new ApiFactory();using var client=factory.CreateClient();
        var body=new {symbol="^TWII",name="TAIEX",market="TW"};
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/market-reference-instruments",body)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Market-Signal","web");
        var response=await client.PostAsJsonAsync("/api/market-reference-instruments",body);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var instrument=(await response.Content.ReadFromJsonAsync<ReferenceInstrument>())!;
        var route="/api/products/00631L/references";
        var created=await client.PostAsJsonAsync(route,new {instrumentId=instrument.Id,referenceType="BROAD_MARKET"});
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var mapping=(await created.Content.ReadFromJsonAsync<ProductMarketReference>())!;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(route,new {instrumentId=instrument.Id,referenceType="BROAD_MARKET"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(route,new {instrumentId=instrument.Id,referenceType="BUY"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync(route,new {instrumentId=99999,referenceType="SECTOR"})).StatusCode);
        var updated=await client.PutAsJsonAsync(route+"/"+mapping.Id,new {referenceId=9999,instrumentId=instrument.Id,referenceType="SECTOR"});
        Assert.Equal(HttpStatusCode.OK,updated.StatusCode);Assert.Equal(mapping.Id,(await updated.Content.ReadFromJsonAsync<ProductMarketReference>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<ProductMarketReference[]>(route))!);
        Assert.Equal(HttpStatusCode.Conflict,(await client.DeleteAsync("/api/market-reference-instruments/"+instrument.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/products/00631L/analysis")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync(route+"/"+mapping.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.DeleteAsync("/api/market-reference-instruments/"+instrument.Id)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<ProductMarketReference[]>(route))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchReusesReferenceEvenOnFailureAndStartsFreshNextRun(bool fail)
    {
        var signals=new MemorySignalStore();var store=new MemoryMarketStore(signals);var references=new MemoryMarketReferenceStore();
        var instrument=await references.SaveInstrumentAsync(1,null,"^TWII","TAIEX","TW",default);
        foreach(var product in await store.GetActiveTrackedProductsAsync("TW",default)) await references.SaveReferenceAsync(1,product.Id,null,instrument.Id,"BROAD_MARKET",default);
        var market=new Market {FailReference=fail};var gpt=new CaptureAnalyst("openai");var deepseek=new CaptureAnalyst("deepseek");
        var runner=new MarketAnalysisRunner(market,store,signals,[gpt,deepseek],NullLogger<MarketAnalysisRunner>.Instance,references);
        var result=await runner.RunAllAsync(default);Assert.Empty(result.Failed);
        Assert.Equal(1,market.ReferenceQuoteCalls);Assert.Equal(1,market.ReferenceHistoryCalls);
        Assert.Equal(3,gpt.Contexts.Count);Assert.Equal(3,deepseek.Contexts.Count);
        foreach(var context in gpt.Contexts)
        {
            Assert.Same(context,deepseek.Contexts.Single(x=>x.Product.Id==context.Product.Id));
            var reference=Assert.Single(context.MarketReferences);Assert.Equal(fail ? "UNAVAILABLE" : "AVAILABLE",reference.Status);
            if(!fail){Assert.Equal(100m,reference.CurrentValue);Assert.Equal(2m,reference.Change);Assert.Equal(2.0408m,reference.ChangePercent);}
        }
        var records=await signals.GetHistoryAsync(1,1,default);
        var inputs=records.Where(x=>x.Model=="captured-live").Select(x=>JsonDocument.Parse(x.InputSnapshotJson).RootElement.GetProperty("analysisInput").GetRawText()).ToArray();
        Assert.Equal(2,inputs.Length);Assert.Equal(inputs[0],inputs[1]);
        Assert.Contains("marketReferences",inputs[0]);
        if(!fail) { Assert.Contains("quoteMetadata",inputs[0]);Assert.Contains("CONTRACTS",inputs[0]); }
        await runner.RunProductAsync("00631L",default);Assert.Equal(2,market.ReferenceQuoteCalls);Assert.Equal(2,market.ReferenceHistoryCalls);
        Assert.Equal(2,gpt.Contexts.Last().PreviousDecisions.Count);
        Assert.Same(gpt.Contexts.Last(),deepseek.Contexts.Last());
    }

    [Fact]
    public async Task EditedMappingIsSharedByProvidersOnNextRunAndOldSnapshotIsPreserved()
    {
        var signals=new MemorySignalStore();var references=new MemoryMarketReferenceStore();
        var instrument=await references.SaveInstrumentAsync(1,null,"^TWII","TAIEX","TW",default);
        var mapping=await references.SaveReferenceAsync(1,1,null,instrument.Id,"BROAD_MARKET",default);
        var gpt=new CaptureAnalyst("openai");var deepseek=new CaptureAnalyst("deepseek");
        var runner=new MarketAnalysisRunner(new Market(),new MemoryMarketStore(signals),signals,[gpt,deepseek],NullLogger<MarketAnalysisRunner>.Instance,references);
        await runner.RunProductAsync("00631L",default);
        var firstSnapshot=(await signals.GetHistoryAsync(1,1,default)).First(x=>x.Model=="captured-live").InputSnapshotJson;
        await references.SaveInstrumentAsync(1,instrument.Id,"^SOX","SOX","US",default);
        await references.SaveReferenceAsync(1,1,mapping.Id,instrument.Id,"SECTOR",default);
        await runner.RunProductAsync("00631L",default);
        Assert.Same(gpt.Contexts.Last(),deepseek.Contexts.Last());
        var current=Assert.Single(gpt.Contexts.Last().MarketReferences);
        Assert.Equal("^SOX",current.Symbol);Assert.Equal("SECTOR",current.ReferenceType);
        Assert.Contains("^TWII",firstSnapshot);Assert.DoesNotContain("^SOX",firstSnapshot);
        await references.DeleteReferenceAsync(1,1,mapping.Id,default);
        await runner.RunProductAsync("00631L",default);
        Assert.Empty(gpt.Contexts.Last().MarketReferences);Assert.Same(gpt.Contexts.Last(),deepseek.Contexts.Last());
    }
    [Fact]
    public async Task NoMappingDoesNotGuessBenchmarks()
    {
        var signals=new MemorySignalStore();var market=new Market();var capture=new CaptureAnalyst("openai");
        var runner=new MarketAnalysisRunner(market,new MemoryMarketStore(signals),signals,[capture],NullLogger<MarketAnalysisRunner>.Instance,new MemoryMarketReferenceStore());
        await runner.RunProductAsync("00631L",default);Assert.Empty(Assert.Single(capture.Contexts).MarketReferences);Assert.Equal(0,market.ReferenceQuoteCalls);
    }
    [Fact]
    public async Task BothLiveAdaptersReceiveIdenticalSerializedInput()
    {
        var context=OpenAIAnalystTests.Context() with
        {
            MarketReferences=[new(1,"UNDERLYING","^TSE50","Taiwan50","TW",new("^TSE50",100,99,101,98,98,0,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow) { QuoteMetadata = new("YAHOO_TW", "^TSE50", "CLOSED", "INDEX_POINTS", "CONTRACTS", 0, "LATEST_CLOSED_QUOTE", "Test metadata") },[],"PARTIAL","Sparse history")],
            PreviousDecisions=[new("openai","previous-live",DateTime.UtcNow,DemoData.MockResult("HOLD",true))]
        };
        string? gptInput=null,deepseekInput=null;
        using var gptHttp=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>
        {using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));gptInput=body.RootElement.GetProperty("input").GetString();Assert.Contains("MarketReferences are evidence",body.RootElement.GetProperty("instructions").GetString()!);return OpenAIAnalystTests.Json(OpenAIAnalystTests.Response());}));
        using var deepHttp=new HttpClient(new OpenAIAnalystTests.Handler(async(req,ct)=>
        {using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));deepseekInput=body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());}));
        await new OpenAIAnalyst(gptHttp,OpenAIAnalystTests.Options()).AnalyzeAsync(context,default);
        await new DeepSeekAnalyst(deepHttp,DeepSeekAnalystTests.Options()).AnalyzeAsync(context,default);
        Assert.Equal(gptInput,deepseekInput);Assert.Contains("^TSE50",gptInput!);Assert.Contains("previousDecisions",gptInput!);Assert.Contains("quoteMetadata",gptInput!);Assert.Contains("LATEST_CLOSED_QUOTE",gptInput!);
    }
    private sealed class CaptureAnalyst(string code) : IAIAnalyst
    {
        public string ProviderCode=>code;public List<MarketContext> Contexts{get;}=[];
        public Task<AnalystResult> AnalyzeAsync(MarketContext context,CancellationToken ct)
        {Contexts.Add(context);return Task.FromResult(new AnalystResult("captured-live",DemoData.MockResult("HOLD",true),"test response"));}
    }
    private sealed class Market : IMarketDataProvider
    {
        public int ReferenceQuoteCalls,ReferenceHistoryCalls;public bool FailReference;
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol,CancellationToken ct)=>Task.FromResult(new MarketSnapshot(symbol,40,39,41,38,39,100,DateTimeOffset.Parse("2026-10-01T05:30:00Z"),DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol,DateOnly from,DateOnly through,CancellationToken ct)=>Task.FromResult<IReadOnlyList<HistoricalPrice>>([]);
        public Task<bool> IsTradingDayAsync(DateOnly date,CancellationToken ct)=>Task.FromResult(true);
        public Task<MarketSnapshot> GetReferenceSnapshotAsync(string symbol,CancellationToken ct)
        {ReferenceQuoteCalls++;if(FailReference)throw new HttpRequestException("upstream",null,HttpStatusCode.NotFound);return Task.FromResult(new MarketSnapshot(symbol,100,99,101,98,98,0,DateTimeOffset.Parse("2026-10-01T05:30:00Z"),DateTimeOffset.UtcNow) { QuoteMetadata = new("YAHOO_TW", symbol, "CLOSED", "INDEX_POINTS", "CONTRACTS", 0, "LATEST_CLOSED_QUOTE", "Test evidence") });}
        public Task<IReadOnlyList<HistoricalPrice>> GetReferenceHistoricalPricesAsync(string symbol,DateOnly from,DateOnly through,CancellationToken ct)
        {ReferenceHistoryCalls++;return Task.FromResult<IReadOnlyList<HistoricalPrice>>([new(through.AddDays(-1),98,99,97,98,0),new(through,99,101,98,100,0)]);}
    }
}
