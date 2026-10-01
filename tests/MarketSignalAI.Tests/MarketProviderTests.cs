using System.Net;
using System.Text;
using MarketSignalAI.Infrastructure;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class MarketProviderTests
{
    [Fact]
    public async Task YahooSnapshot_NormalizesLeveragedEtf_WithoutExposingYahooJson()
    {
        Uri? requested = null;
        var http = new HttpClient(new DelegateHandler(req =>
        {
            requested = req.RequestUri;
            return """
                {"chart":{"result":[{"meta":{"regularMarketTime":1790746201,"regularMarketPrice":39.0,
                "regularMarketDayHigh":39.5,"regularMarketDayLow":38.9,"chartPreviousClose":38.2,
                "regularMarketVolume":123456},"indicators":{"quote":[{"open":[null,39.1,39.2]}]}}],"error":null}}
                """;
        }));
        var snapshot = await new YahooMarketDataProvider(http).GetSnapshotAsync("00631L", default);
        Assert.Contains("/00631L.TW", requested!.AbsoluteUri);
        Assert.Equal(39.1m, snapshot.Open);
        Assert.Equal(123456, snapshot.Volume);
        Assert.Equal("00631L", snapshot.Symbol);
        Assert.Equal(DateTimeOffset.UtcNow.Date, snapshot.FetchedAt.Date);
    }

    [Theory]
    [InlineData("^TSE50")]
    [InlineData("^TWII")]
    public async Task ReferenceTickerIsUsedExactlyAndZeroIndexVolumeIsAccepted(string ticker)
    {
        using var http=new HttpClient(new DelegateHandler(req=>
        {
            Assert.Equal(ticker,Uri.UnescapeDataString(req.RequestUri!.AbsolutePath.Split('/').Last()));
            return """{"chart":{"result":[{"meta":{"regularMarketTime":1790832600,"regularMarketPrice":100,"regularMarketDayHigh":101,"regularMarketDayLow":98,"chartPreviousClose":99,"regularMarketVolume":0},"indicators":{"quote":[{"open":[99]}]}}],"error":null}}""";
        }));
        var quote=await new YahooMarketDataProvider(http).GetReferenceSnapshotAsync(ticker,default);
        Assert.Equal(ticker,quote.Symbol);Assert.Equal(0,quote.Volume);Assert.Equal(100,quote.Price);
    }

    [Fact]
    public async Task TradingDay_UsesTwseCalendar_AndSkipsWeekend()
    {
        var calls = 0;
        var http = new HttpClient(new DelegateHandler(req =>
        {
            calls++;
            Assert.Contains("queryYear=115", req.RequestUri!.Query);
            return """{"stat":"ok","data":[["2026-10-01","休市日","test"]]}""";
        }));
        var provider = new YahooMarketDataProvider(http);
        Assert.False(await provider.IsTradingDayAsync(new(2026, 10, 1), default));
        Assert.False(await provider.IsTradingDayAsync(new(2026, 10, 3), default));
        Assert.True(await provider.IsTradingDayAsync(new(2026, 10, 2), default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task YahooHistory_MapsDailyBars_AndIgnoresMissingBar()
    {
        var http = new HttpClient(new DelegateHandler(_ => """
            {"chart":{"result":[{"timestamp":[1790746200,1790832600],
            "indicators":{"quote":[{"open":[39.099998,null],"high":[39.5,null],
            "low":[38.9,null],"close":[39.0,null],"volume":[123456,null]}]}}],"error":null}}
            """));
        var prices = await new YahooMarketDataProvider(http).GetHistoricalPricesAsync("00631L", new(2026, 9, 30), new(2026, 10, 1), default);
        var bar = Assert.Single(prices);
        Assert.Equal(new DateOnly(2026, 9, 30), bar.TradeDate);
        Assert.Equal(39.1m, bar.Open);
        Assert.Equal(123456, bar.Volume);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"chart\":{\"result\":null,\"error\":{\"code\":\"Not Found\"}}}")]
    [InlineData("{\"chart\":{\"result\":[]}}")]
    [InlineData("{\"chart\":{\"result\":[{}]}}")]
    public async Task YahooMalformedResponse_IsAnUpstreamFailure(string json)
    {
        using var http = new HttpClient(new DelegateHandler(_ => json));
        await Assert.ThrowsAsync<InvalidDataException>(() => new YahooMarketDataProvider(http).GetSnapshotAsync("00631L", default));
    }

    [Theory]
    [InlineData("{\"stat\":\"ok\",\"data\":[]}")]
    [InlineData("{\"stat\":\"ok\",\"data\":[[\"2025-10-01\",\"holiday\"]]}")]
    [InlineData("{\"stat\":\"ok\",\"data\":[[\"invalid-date\",\"holiday\"]]}")]
    [InlineData("{\"stat\":\"ok\",\"data\":[[]]}")]
    public async Task InvalidCalendar_DoesNotAssumeMarketIsOpen(string json)
    {
        using var http = new HttpClient(new DelegateHandler(_ => json));
        await Assert.ThrowsAsync<InvalidDataException>(() => new YahooMarketDataProvider(http).IsTradingDayAsync(new(2026, 10, 1), default));
    }

    [Fact]
    public async Task Calendar_OpeningAndLastTradingDatesRemainOpen()
    {
        using var http = new HttpClient(new DelegateHandler(_ => """
            {"stat":"ok","data":[["2026-01-02","國曆新年開始交易日"],["2026-02-11","農曆春節前最後交易日"]]}
            """));
        var provider = new YahooMarketDataProvider(http);
        Assert.True(await provider.IsTradingDayAsync(new(2026, 1, 2), default));
        Assert.True(await provider.IsTradingDayAsync(new(2026, 2, 11), default));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
