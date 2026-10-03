using System.Net;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketSignalAI.Tests;

internal sealed class OfflineTwMarketContextProvider : ITwMarketContextProvider
{
    public Task<TwMarketContext> GetAsync(string targetSymbol, DateOnly quoteDate, CancellationToken ct) =>
        Task.FromResult(TwMarketContext.Unavailable("Offline test fixture; no official market request."));
}

public sealed class TwMarketContextTests
{
    [Fact]
    public async Task OfficialReportsAreParsedWithDatesUnitsAndDeterministicHistory()
    {
        using var http = new HttpClient(new TwseFixture());
        var context = await new TwseMarketContextProvider(http, NullLogger<TwseMarketContextProvider>.Instance)
            .GetAsync("00631L", new DateOnly(2026, 10, 2), default);
        Assert.Equal("AVAILABLE", context.Turnover.Evidence.Status);
        Assert.Equal("NTD", context.Turnover.Unit);
        Assert.Equal(2100m, context.Turnover.Current);
        Assert.Equal(1900m, context.Turnover.Average5D);
        Assert.Equal(1150m, context.Turnover.Average20D);
        Assert.Equal(82.6087m, context.Turnover.VsAverage20DPercent);
        Assert.Equal(483, context.Breadth.Advancers);
        Assert.Equal(506, context.Breadth.Decliners);
        Assert.Equal(91, context.Breadth.Unchanged);
        Assert.Equal(24, context.Breadth.LimitUp);
        Assert.Equal(1, context.Breadth.LimitDown);
        Assert.Equal("TWSE listed stocks", context.Breadth.Universe);
        Assert.Equal("NTD_thousands", context.Margin.MarketFinancing.Unit);
        Assert.Equal(635104422, context.Margin.MarketFinancing.Current);
        Assert.Equal(100, context.Margin.TargetFinancing.Change1D);
        Assert.Equal(500, context.Margin.TargetFinancing.Change5D);
        Assert.Equal(2000, context.Margin.TargetFinancing.Change20D);
        Assert.Equal("TWSE_trading_units", context.Margin.TargetShortSelling.Unit);
        Assert.Equal("NTD", context.InstitutionalFlow.MarketForeign.Unit);
        Assert.Equal("shares", context.InstitutionalFlow.TargetForeign.Unit);
        Assert.Equal(20, context.InstitutionalFlow.MarketForeign.Cumulative20D);
        Assert.Equal(-20, context.InstitutionalFlow.TargetForeign.Cumulative20D);
    }

    [Fact]
    public async Task PreviousDayReportsAreStaleAndMissingSectionsStayUnknown()
    {
        using var http = new HttpClient(new TwseFixture(failTargetFlow: true));
        var context = await new TwseMarketContextProvider(http, NullLogger<TwseMarketContextProvider>.Instance)
            .GetAsync("00631L", new DateOnly(2026, 10, 5), default);
        Assert.Equal("STALE", context.Turnover.Evidence.Status);
        Assert.Equal(new DateOnly(2026, 10, 2), context.Breadth.Evidence.MarketDate);
        Assert.Equal("STALE", context.Breadth.Evidence.Status);
        Assert.Contains("Never describe this as today's data", context.Breadth.Evidence.Reason);
        Assert.Equal("UNAVAILABLE", context.InstitutionalFlow.TargetForeign.Evidence.Status);
        Assert.Null(context.InstitutionalFlow.TargetForeign.Today);
        Assert.NotNull(context.InstitutionalFlow.MarketForeign.Today);
    }

    [Fact]
    public async Task MissingTargetMarginDoesNotDiscardMarketMargin()
    {
        using var http = new HttpClient(new TwseFixture(missingTargetMargin: true));
        var context = await new TwseMarketContextProvider(http, NullLogger<TwseMarketContextProvider>.Instance)
            .GetAsync("00631L", new DateOnly(2026, 10, 2), default);
        Assert.Equal(635104422, context.Margin.MarketFinancing.Current);
        Assert.Equal("UNAVAILABLE", context.Margin.TargetFinancing.Evidence.Status);
        Assert.Null(context.Margin.TargetFinancing.Current);
        Assert.Null(context.Margin.TargetShortSelling.Change1D);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunnerFreezesOneContextForBothProvidersAndPersistsTheSnapshot(bool sourceFails)
    {
        var signals = new MemorySignalStore();
        var source = new ContextSource(sourceFails);
        var gpt = new CapturingAnalyst("openai");
        var deepseek = new CapturingAnalyst("deepseek");
        var runner = AnalysisRunnerFixture.Create(new Market(), new MemoryMarketStore(signals), signals,
            [gpt, deepseek], NullLogger<MarketAnalysisRunner>.Instance, twMarket: source);
        var run = await runner.RunProductAsync("00631L", ["openai", "deepseek"], default);
        Assert.All(run.Providers, x => Assert.Equal("COMPLETED", x.Status));
        Assert.Equal(1, source.Calls);
        Assert.Same(Assert.Single(gpt.Contexts), Assert.Single(deepseek.Contexts));
        var shared = gpt.Contexts[0].TwMarketContext!;
        Assert.Equal(sourceFails ? "UNAVAILABLE" : "AVAILABLE", shared.Turnover.Evidence.Status);
        var records = (await signals.GetHistoryAsync(1, 1, default))
            .Where(x => x.Model == "tw-captured").ToArray();
        Assert.Equal(2, records.Length);
        var inputs = records.Select(x => JsonDocument.Parse(x.InputSnapshotJson)).ToArray();
        try
        {
            var left = inputs[0].RootElement.GetProperty("analysisInput").GetRawText();
            var right = inputs[1].RootElement.GetProperty("analysisInput").GetRawText();
            Assert.Equal(left, right);
            Assert.Contains("twMarketContext", left);
            Assert.Contains("targetReturns", left);
            var current = inputs[0].RootElement.GetProperty("twMarketContext")
                .GetProperty("Turnover").GetProperty("Current");
            Assert.Equal(shared.Turnover.Current, current.ValueKind == JsonValueKind.Null ? null : current.GetDecimal());
        }
        finally { foreach (var input in inputs) input.Dispose(); }
    }

    [Fact]
    public async Task LiveAdaptersReceiveIdenticalTwContextAndDoNotFetchMarketData()
    {
        var source = new TwseMarketContextProvider(new HttpClient(new TwseFixture()), NullLogger<TwseMarketContextProvider>.Instance);
        var tw = await source.GetAsync("00631L", new DateOnly(2026, 10, 2), default);
        var context = OpenAIAnalystTests.Context() with { TwMarketContext = tw };
        string? openAiInput = null, deepSeekInput = null;
        using var openAiHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (request, ct) =>
        {
            Assert.Equal("api.openai.com", request.RequestUri!.Host);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            openAiInput = body.RootElement.GetProperty("input").GetString();
            Assert.Contains("yesterday's report", body.RootElement.GetProperty("instructions").GetString());
            return OpenAIAnalystTests.Json(OpenAIAnalystTests.Response());
        }));
        using var deepSeekHttp = new HttpClient(new OpenAIAnalystTests.Handler(async (request, ct) =>
        {
            Assert.Contains("deepseek", request.RequestUri!.Host);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            deepSeekInput = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            return OpenAIAnalystTests.Json(DeepSeekAnalystTests.Response());
        }));
        await new OpenAIAnalyst(openAiHttp, OpenAIAnalystTests.Options()).AnalyzeAsync(context, default);
        await new DeepSeekAnalyst(deepSeekHttp, DeepSeekAnalystTests.Options()).AnalyzeAsync(context, default);
        Assert.Equal(openAiInput, deepSeekInput);
        Assert.Contains("twMarketContext", openAiInput);
        Assert.Contains("635104422", openAiInput);
    }

    private sealed class Market : IMarketDataProvider
    {
        public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) =>
            Task.FromResult(OpenAIAnalystTests.Context().Snapshot with { Symbol = symbol,
                MarketTime = DateTimeOffset.Parse("2026-10-02T05:30:00Z") });
        public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) =>
            Task.FromResult(OpenAIAnalystTests.Context().History);
        public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class ContextSource(bool fail) : ITwMarketContextProvider
    {
        public int Calls;
        public Task<TwMarketContext> GetAsync(string symbol, DateOnly quoteDate, CancellationToken ct)
        {
            Calls++;
            if (fail) throw new HttpRequestException("official source unavailable");
            return Task.FromResult(TwMarketContext.Unavailable("test") with
            { Turnover = new(new("TWSE /exchangeReport/FMTQIK", quoteDate,
                DateTimeOffset.UtcNow, "AVAILABLE", "EOD"), "NTD", 123m, null, null, null, null) });
        }
    }
    private sealed class CapturingAnalyst(string code) : IAIAnalyst
    {
        public string ProviderCode => code;
        public List<MarketContext> Contexts { get; } = [];
        public Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return Task.FromResult(new AnalystResult("tw-captured", DemoData.MockResult("HOLD", true), "test"));
        }
    }

    private sealed class TwseFixture(bool failTargetFlow = false, bool missingTargetMargin = false) : HttpMessageHandler
    {
        private static readonly DateOnly[] Days = Enumerable.Range(0, 21)
            .Select(i => new DateOnly(2026, 10, 2).AddDays(-i))
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .Concat(Enumerable.Range(0, 21).Select(i => new DateOnly(2026, 9, 4).AddDays(-i))
                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday))
            .OrderByDescending(d => d).Take(21).ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var rawDate = query["date"] ?? query["dayDate"] ?? "";
            var date = rawDate.Length == 8 ? DateOnly.ParseExact(rawDate, "yyyyMMdd") : default;
            var index = Array.IndexOf(Days, date);
            object body;
            if (url.AbsolutePath.EndsWith("/FMTQIK"))
                body = new { stat = "OK", fields = new[] { "Date", "Trade Volume", "Trade Value" },
                    data = Days.Where(d => d.Month == int.Parse(rawDate.Substring(4, 2)))
                        .Select(d => new[] { d.ToString("yyyy/MM/dd"), "1,000", (100 * (21 - Array.IndexOf(Days, d))).ToString() }).ToArray() };
            else if (url.AbsolutePath.EndsWith("/MI_INDEX"))
            {
                if (index < 0) body = new { stat = "No data" };
                else body = new { stat = "OK", date = rawDate, tables = new object[] { new { fields = Array.Empty<string>() }, new {
                    title = "Net Change of Price (Number of Listed Securities)", fields = new[] { "Type", "Overall Market", "Stocks" },
                    data = new[] { new[] { "Up (Limit Up)", "8,830(80)", "483(24)" }, new[] { "Down (Limit Down)", "5,152(10)", "506(1)" }, new[] { "Unchanged", "1,000", "91" } } } } };
            }
            else if (url.AbsolutePath.EndsWith("/MI_MARGN"))
            {
                var margin = index >= 0 ? 184070 - 100 * index : 0;
                body = index < 0 ? new { stat = "No data" } : (object)new { stat = "OK", date = rawDate, tables = new object[] {
                    new { title = "Margin transaction summary", fields = new[] { "Item", "Margin Purchase/ Short Covering" },
                        data = new[] { new[] { "Margin Purchase Value (In thousands)", "", "", "", "635104322", "635104422" } } },
                    new { title = "Margin Transactions (All)", fields = new[] { "Security Code", "Margin Purchase" },
                        data = missingTargetMargin ? Array.Empty<string[]>() : new[] { new[] { "00631L", "", "", "", (margin - 100).ToString(), margin.ToString(), "", "", "", "", "100", "99" } } } } };
            }
            else if (url.AbsolutePath.EndsWith("/BFI82U"))
                body = index < 0 ? new { stat = "No data" } : (object)new { stat = "OK", date = rawDate,
                    fields = new[] { "Item", "Total Buy", "Total Sell", "Difference" }, data = new[] {
                        new[] { "Foreign Investors include Mainland Area Investors(Foreign Dealers excluded)", "", "", "1" },
                        new[] { "Securities Investment Trust Companies", "", "", "2" },
                        new[] { "Dealers (Proprietary)", "", "", "3" }, new[] { "Dealers (Hedge)", "", "", "-1" } } };
            else if (url.AbsolutePath.EndsWith("/T86"))
            {
                if (failTargetFlow) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                var fields = new[] { "Security Code", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "Total Difference" };
                var row = new string[18]; row[0] = "00631L"; row[3] = "-1"; row[9] = "2"; row[10] = "-3";
                body = index < 0 ? new { stat = "No data" } : (object)new { stat = "OK", date = rawDate, fields, data = new[] { row } };
            }
            else throw new InvalidOperationException(url.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
        }
    }
}
