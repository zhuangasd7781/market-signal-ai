using System.Net;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using Xunit;

namespace MarketSignalAI.Tests;

public sealed class YahooTaiwanIntradayTests
{
    private static string Quote(string state = "open", string symbol = "WTX&", DateTimeOffset? time = null) => JsonSerializer.Serialize(new
    {
        data = new[] { new { symbol, marketStatus = state, regularMarketTime = (time ?? DateTimeOffset.UtcNow.AddSeconds(-15)).ToString("O"),
            price = new { raw = "49346" }, regularMarketOpen = new { raw = "48671" },
            regularMarketDayHigh = new { raw = "49495" }, regularMarketDayLow = new { raw = "48671" },
            regularMarketPreviousClose = new { raw = "48669" }, volume = "32209", exchangeDataDelayedBy = 0 } }
    });

    [Theory]
    [InlineData("open", "INTRADAY")]
    [InlineData("close", "LATEST_CLOSED_QUOTE")]
    [InlineData("unexpected", "UNKNOWN")]
    public async Task IntradayAndClosedQuotesPreserveTimeSourceAndContractUnits(string state, string quality)
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((req, ct) =>
        {
            Assert.Equal("tw.stock.yahoo.com", req.RequestUri!.Host);
            Assert.Contains("symbols=WTX%26", req.RequestUri.OriginalString);
            return Task.FromResult(OpenAIAnalystTests.Json(Quote(state)));
        }));
        var quote = await new YahooMarketDataProvider(http).GetReferenceSnapshotAsync("WTX&", default);
        Assert.Equal(49346m, quote.Price); Assert.Equal(677m, quote.Price - quote.PreviousClose);
        Assert.Equal(32209, quote.Volume); Assert.Equal("CONTRACTS", quote.QuoteMetadata!.VolumeUnit);
        Assert.Equal("INDEX_POINTS", quote.QuoteMetadata.PriceUnit); Assert.Equal("YAHOO_TW", quote.QuoteMetadata.Source);
        Assert.Equal(quality, quote.QuoteMetadata.DataQuality); Assert.Equal(0, quote.QuoteMetadata.DelayMinutes);
        Assert.True(quote.MarketTime < quote.FetchedAt);
    }

    [Fact]
    public async Task OldOpenQuoteIsStaleRatherThanLive()
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((req, ct) => Task.FromResult(OpenAIAnalystTests.Json(Quote(time: DateTimeOffset.UtcNow.AddDays(-1))))));
        Assert.Equal("STALE", (await new YahooMarketDataProvider(http).GetReferenceSnapshotAsync("WTX&", default)).QuoteMetadata!.DataQuality);
    }

    [Theory]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"symbol\":\"OTHER\"}]}")]
    [InlineData("{\"data\":[{\"symbol\":\"WTX&\"}]}")]
    public async Task MissingOrMismatchedQuoteFailsWithoutFallback(string body)
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((req, ct) => Task.FromResult(OpenAIAnalystTests.Json(body))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new YahooMarketDataProvider(http).GetReferenceSnapshotAsync("WTX&", default));
    }

    [Fact]
    public async Task HttpFailureIsNotSubstitutedWithDailyOrMockData()
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new YahooMarketDataProvider(http).GetReferenceSnapshotAsync("WTX&", default));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task UnverifiedHistoryIsExplicitlyAbsentAndDoesNotFetchAnotherTicker()
    {
        using var http = new HttpClient(new OpenAIAnalystTests.Handler((req, ct) => throw new Xunit.Sdk.XunitException("No history endpoint should be called.")));
        var data = await new YahooMarketDataProvider(http).GetReferenceHistoryAsync("WTX&", new(2026, 9, 1), new(2026, 10, 1), default);
        Assert.Empty(data.Prices); Assert.Equal("INSUFFICIENT", data.Metadata.DataQuality);
        Assert.Equal("WTX&", data.Metadata.SourceSymbol); Assert.False(data.Metadata.IsFallback);
        Assert.Contains("Intraday quote is available", data.Metadata.Reason);
    }

    [Fact]
    public async Task TickerCanBeManagedThroughExistingCrud()
    {
        var store = new MemoryMarketReferenceStore();
        var instrument = await store.SaveInstrumentAsync(1, null, "WTX&", "Taiwan near-month index futures", "TW", default);
        var mapping = await store.SaveReferenceAsync(1, 1, null, instrument.Id, "BROAD_MARKET", default);
        Assert.Equal("WTX&", mapping.Instrument.Symbol);
        Assert.Throws<ArgumentException>(() => MarketReferenceValidation.YahooSymbol("WTX&?url=bad"));
    }
}
