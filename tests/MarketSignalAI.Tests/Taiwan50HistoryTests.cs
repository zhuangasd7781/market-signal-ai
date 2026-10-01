using System.Net;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class Taiwan50HistoryTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly Through = new(2026, 10, 1);
    private const string SparseYahoo = """{"chart":{"result":[{"timestamp":[1790832890],"indicators":{"quote":[{"open":[44560.04],"high":[45041.66],"low":[44560.04],"close":[45041.66],"volume":[0]}]}}],"error":null}}""";
    private const string Quote = """{"chart":{"result":[{"meta":{"regularMarketTime":1790832600,"regularMarketPrice":45041.66,"regularMarketDayHigh":45041.66,"regularMarketDayLow":44560.04,"chartPreviousClose":44560.04,"regularMarketVolume":0},"indicators":{"quote":[{"open":[44560.04]}]}}],"error":null}}""";
    private static string Official(string rows) => """{"stat":"OK","title":"臺灣50指數歷史資料","fields":["日期","臺灣50指數","臺灣50報酬指數"],"data":ROWS}""".Replace("ROWS", rows);
    private static string September => Official("""[["115/09/01","43,491.42","100,445.49"],["115/09/30","44,560.04","103,089.40"]]""");
    private static string October => Official("""[["115/10/01","45,041.66","104,203.60"]]""");
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static string Monthly(HttpRequestMessage req) => req.RequestUri!.Query.Contains("date=20260901") ? September : October;

    [Fact]
    public async Task SparseYahooUsesOfficialPriceIndexClosesWithExplicitProvenanceAndNullMissingFields()
    {
        var urls = new List<Uri>();
        using var http = new HttpClient(new Handler(req =>
        {
            urls.Add(req.RequestUri!);
            return Json(req.RequestUri!.Host.Contains("yahoo") ? SparseYahoo : Monthly(req));
        }));
        var data = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("^TSE50", From.AddDays(1), Through, default);
        Assert.Equal(3, urls.Count);
        Assert.All(urls.Skip(1), u => Assert.Equal("/indicesReport/TAI50I", u.AbsolutePath));
        Assert.Contains("period1=", urls[0].Query); Assert.Contains("interval=1d", urls[0].Query);
        Assert.Equal(2, data.Prices.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), data.Prices[0].TradeDate);
        Assert.Equal(44560.04m, data.Prices[0].Close); // Never the return index (103089.40).
        Assert.Equal(45041.66m, data.Prices[1].Close);
        Assert.All(data.Prices, p => { Assert.Null(p.Open); Assert.Null(p.High); Assert.Null(p.Low); Assert.Null(p.Volume); });
        Assert.Equal("TWSE", data.Metadata.Source); Assert.Equal("TAI50I", data.Metadata.SourceSymbol);
        Assert.True(data.Metadata.IsFallback); Assert.Equal("CLOSE_ONLY", data.Metadata.DataQuality);
        Assert.Contains("only 1", data.Metadata.Reason!); Assert.Equal(2, data.Metadata.Attempts.Count);
        Assert.Equal(1, data.Metadata.Attempts[0].Count); Assert.Equal(2, data.Metadata.Attempts[1].Count);
        Assert.Equal(From.AddDays(1), data.Metadata.From); Assert.Equal(Through, data.Metadata.Through);
    }

    [Fact]
    public async Task YahooHttpFailureCanUseOfficialSourceAndRecordsActualStatus()
    {
        using var http = new HttpClient(new Handler(req => req.RequestUri!.Host.Contains("yahoo") ? new(HttpStatusCode.TooManyRequests) : Json(Monthly(req))));
        var result = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("^TSE50", From, Through, default);
        Assert.True(result.Metadata.IsFallback); Assert.Equal(3, result.Prices.Count);
        Assert.Contains("HTTP 429", result.Metadata.Reason!);
        Assert.Null(result.Metadata.Attempts[0].Count);
    }

    [Fact]
    public async Task FailedFallbackRetainsRealSparseYahooAndExposesFailureWithoutBody()
    {
        using var http = new HttpClient(new Handler(req => req.RequestUri!.Host.Contains("yahoo") ? Json(SparseYahoo) :
            new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("sensitive upstream content") }));
        var result = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("^TSE50", From, Through, default);
        Assert.Single(result.Prices); Assert.Equal("YAHOO", result.Metadata.Source);
        Assert.False(result.Metadata.IsFallback); Assert.Equal("INSUFFICIENT", result.Metadata.DataQuality);
        Assert.Contains("HTTP 503", result.Metadata.Reason!); Assert.DoesNotContain("sensitive", result.Metadata.Reason!);
    }

    [Theory]
    [InlineData("wrong-index")]
    [InlineData("return-column")]
    [InlineData("wrong-month")]
    [InlineData("duplicate")]
    [InlineData("bad-date")]
    [InlineData("negative-close")]
    [InlineData("malformed")]
    public async Task InvalidOfficialReportNeverBecomesSuccessfulHistory(string scenario)
    {
        var report = scenario switch
        {
            "wrong-index" => September.Replace("臺灣50指數歷史資料", "臺灣中型100指數歷史資料"),
            "return-column" => September.Replace("[\"日期\",\"臺灣50指數\",", "[\"日期\",\"臺灣50報酬指數\","),
            "wrong-month" => September.Replace("115/09/01", "115/08/01"),
            "duplicate" => Official("""[["115/09/01","1","2"],["115/09/01","1","2"]]"""),
            "bad-date" => September.Replace("115/09/01", "115/09/99"),
            "negative-close" => September.Replace("43,491.42", "-1"),
            _ => "not json"
        };
        using var http = new HttpClient(new Handler(req => Json(req.RequestUri!.Host.Contains("yahoo") ? SparseYahoo : report)));
        var result = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("^TSE50", From, Through, default);
        Assert.False(result.Metadata.IsFallback); Assert.Single(result.Prices);
        Assert.NotNull(result.Metadata.Attempts[1].Error); Assert.Equal("INSUFFICIENT", result.Metadata.DataQuality);
    }

    [Fact]
    public async Task InsufficientOfficialHistoryIsNotSelectedOrInvented()
    {
        using var http = new HttpClient(new Handler(req => Json(req.RequestUri!.Host.Contains("yahoo") ? SparseYahoo :
            req.RequestUri.Query.Contains("date=20260901") ? Official("[]") : October)));
        var result = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("^TSE50", From, Through, default);
        Assert.False(result.Metadata.IsFallback); Assert.Single(result.Prices);
        Assert.Contains("insufficient fallback was not selected", result.Metadata.Reason!);
    }

    [Theory]
    [InlineData("^TSE50", false)]
    [InlineData("^TWII", true)]
    public async Task SufficientYahooOrUnmappedSourceNeverCallsTwse(string symbol, bool sparse)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(req =>
        {
            calls++; Assert.Contains("yahoo", req.RequestUri!.Host);
            return Json(sparse ? SparseYahoo : """{"chart":{"result":[{"timestamp":[1790746200,1790832600],"indicators":{"quote":[{"open":[99,100],"high":[101,102],"low":[98,99],"close":[100,101],"volume":[0,0]}]}}],"error":null}}""");
        }));
        var result = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync(symbol, From, Through, default);
        Assert.Equal(1, calls); Assert.False(result.Metadata.IsFallback); Assert.Equal("YAHOO", result.Metadata.Source);
        Assert.Equal(sparse ? 1 : 2, result.Prices.Count);
    }

    [Fact]
    public async Task YahooParsingKeepsAllValidTaipeiDatesWithoutTradingDayFiltering()
    {
        // UTC previous-day 16:00 is Taipei midnight; only outside range/null bars are filtered.
        var dates = new[] { new DateTimeOffset(2026, 9, 29, 16, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero) };
        var json = JsonSerializer.Serialize(new { chart = new { result = new[] { new { timestamp = dates.Select(x => x.ToUnixTimeSeconds()),
            indicators = new { quote = new[] { new { open = new decimal?[] { 100, null, 101, 102 }, high = new[] { 101, 101, 102, 103 },
                low = new[] { 99, 99, 100, 101 }, close = new[] { 100, 100, 101, 102 }, volume = new[] { 0, 0, 0, 0 } } } } } }, error = (string?)null } });
        using var http = new HttpClient(new Handler(_ => Json(json)));
        var prices = await new YahooMarketDataProvider(http).GetReferenceHistoricalPricesAsync("^TSE50", new(2026, 9, 30), Through, default);
        Assert.Equal(2, prices.Count); Assert.Equal(new DateOnly(2026, 9, 30), prices[0].TradeDate); Assert.Equal(Through, prices[1].TradeDate);
    }

    [Fact]
    public async Task RealAdaptersShareFallbackContextAndPersistProvenanceWithBatchReuse()
    {
        var sourceCalls = 0;
        using var http = new HttpClient(new Handler(req =>
        {
            sourceCalls++;
            return Json(req.RequestUri!.Host.Contains("yahoo") ? req.RequestUri.Query.Contains("interval=1m") ? Quote : SparseYahoo : Monthly(req));
        }));
        var signals = new MemorySignalStore(); var store = new MemoryMarketStore(signals); var references = new MemoryMarketReferenceStore();
        var instrument = await references.SaveInstrumentAsync(1, null, "^TSE50", "Taiwan50", "TW", default);
        foreach (var product in await store.GetActiveTrackedProductsAsync("TW", default))
            await references.SaveReferenceAsync(1, product.Id, null, instrument.Id, "UNDERLYING", default);
        var inputs = new List<string>();
        using var gptHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (req, ct) =>
        {
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            var input = body.RootElement.GetProperty("input").GetString()!;
            inputs.Add(input);
            Assert.Contains("CLOSE_ONLY", input); Assert.Contains("TWSE", input);
            return OpenAIAnalystTests.Json(OpenAIAnalystTests.Response());
        }));
        using var deepHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (req, ct) =>
        {
            using var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct));
            var input = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            inputs.Add(input);
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());
        }));
        var runner = new MarketAnalysisRunner(new TargetWithReferences(new YahooMarketDataProvider(http)), store, signals,
            [new OpenAIAnalyst(gptHttp, OpenAIAnalystTests.Options()), new DeepSeekAnalyst(deepHttp, DeepSeekAnalystTests.Options()), new MockAIAnalyst("claude")],
            NullLogger<MarketAnalysisRunner>.Instance, references);
        var batch = await runner.RunAllAsync(default);
        Assert.Empty(batch.Failed); Assert.All(batch.Completed.SelectMany(x => x.Providers), x => Assert.Equal("COMPLETED", x.Status));
        Assert.Equal(4, sourceCalls); // One quote, one Yahoo history and two official months for the whole batch.
        Assert.Equal(6, inputs.Count);
        for (var i = 0; i < inputs.Count; i += 2) Assert.Equal(inputs[i], inputs[i + 1]);
        var records = await signals.GetHistoryAsync(1, 1, default);
        var saved = records.Where(x => x.Model is "gpt-6.1-sol" or "reported-deepseek").ToArray();
        Assert.Equal(2, saved.Length);
        foreach (var record in saved)
        {
            using var snapshot = JsonDocument.Parse(record.InputSnapshotJson);
            var reference = snapshot.RootElement.GetProperty("analysisInput").GetProperty("marketReferences")[0];
            Assert.Equal("^TSE50", reference.GetProperty("symbol").GetString());
            Assert.Equal("TWSE", reference.GetProperty("historyMetadata").GetProperty("source").GetString());
            Assert.Equal(3, reference.GetProperty("history").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, reference.GetProperty("history")[0].GetProperty("open").ValueKind);
        }
    }

    private sealed class TargetWithReferences(YahooMarketDataProvider provider) : IMarketDataProvider
    {
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) => Task.FromResult(new MarketSnapshot(symbol, 39, 38, 40, 37, 38, 100, new DateTimeOffset(2026, 10, 1, 5, 30, 0, TimeSpan.Zero), DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => Task.FromResult<IReadOnlyList<HistoricalPrice>>([]);
        public Task<MarketSnapshot> GetReferenceSnapshotAsync(string symbol, CancellationToken ct) => provider.GetReferenceSnapshotAsync(symbol, ct);
        public Task<ReferenceHistoryData> GetReferenceHistoryAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => provider.GetReferenceHistoryAsync(symbol, from, through, ct);
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true);
    }
}
