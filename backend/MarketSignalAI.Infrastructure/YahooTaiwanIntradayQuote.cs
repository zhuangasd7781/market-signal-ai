using System.Globalization;
using System.Text.Json;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

// Yahoo Taiwan's own intraday JSON service; no HTML scraping or daily-price substitution.
internal sealed class YahooTaiwanIntradayQuote(HttpClient http)
{
    public async Task<MarketSnapshot> GetAsync(string symbol, CancellationToken ct)
    {
        var url = $"https://tw.stock.yahoo.com/_td-stock/api/resource/StockServices.stockList;symbols={Uri.EscapeDataString(symbol)};autoRefresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}?returnMeta=true";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        try
        {
            var rows = doc.RootElement.GetProperty("data");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1)
                throw new InvalidDataException("Yahoo Taiwan supplied no unique intraday quote.");
            var q = rows[0];
            if (q.GetProperty("symbol").GetString() != symbol)
                throw new InvalidDataException("Yahoo Taiwan quote symbol mismatch.");
            var state = q.GetProperty("marketStatus").GetString()?.ToUpperInvariant() ?? "UNKNOWN";
            if (state == "CLOSE") state = "CLOSED";
            var marketTime = DateTimeOffset.Parse(q.GetProperty("regularMarketTime").GetString()!, CultureInfo.InvariantCulture);
            var fetchedAt = DateTimeOffset.UtcNow;
            int? delay = q.TryGetProperty("exchangeDataDelayedBy", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null;
            var snapshot = new MarketSnapshot(symbol, Number(q, "price"), Number(q, "regularMarketOpen"),
                Number(q, "regularMarketDayHigh"), Number(q, "regularMarketDayLow"), Number(q, "regularMarketPreviousClose"),
                long.Parse(q.GetProperty("volume").GetString()!, CultureInfo.InvariantCulture), marketTime, fetchedAt)
            {
                QuoteMetadata = new("YAHOO_TW", symbol, state, "INDEX_POINTS", "CONTRACTS", delay,
                    state == "CLOSE" || state == "CLOSED" ? "LATEST_CLOSED_QUOTE" :
                    fetchedAt - marketTime > TimeSpan.FromMinutes((delay ?? 0) + 5) ? "STALE" :
                    state == "OPEN" ? "INTRADAY" : "UNKNOWN",
                    "Rolling near-month index futures; contract month is not supplied by this source. Sessions may include night trading.")
            };
            if (snapshot.Price <= 0 || snapshot.Open <= 0 || snapshot.High <= 0 || snapshot.Low <= 0 ||
                snapshot.PreviousClose <= 0 || snapshot.Volume < 0 || snapshot.Low > snapshot.High ||
                snapshot.Price < snapshot.Low || snapshot.Price > snapshot.High || delay < 0 || marketTime > fetchedAt.AddMinutes(5))
                throw new InvalidDataException("Yahoo Taiwan contains invalid intraday quote values or time.");
            return snapshot;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or ArgumentOutOfRangeException or OverflowException)
        { throw new InvalidDataException("Yahoo Taiwan intraday quote response is invalid.", ex); }
    }

    private static decimal Number(JsonElement row, string field) =>
        decimal.Parse(row.GetProperty(field).GetProperty("raw").GetString()!, CultureInfo.InvariantCulture);
}
