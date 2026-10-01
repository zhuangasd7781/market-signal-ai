using System.Globalization;
using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

// Yahoo chart JSON and the TWSE holiday calendar are confined to this adapter.
public sealed class YahooMarketDataProvider(HttpClient http) : IMarketDataProvider
{
    private static string Ticker(string symbol)
    {
        if (symbol.Length is < 4 or > 7 ||
            !symbol.Take(symbol.Length - (char.IsAsciiLetterUpper(symbol[^1]) ? 1 : 0)).All(char.IsAsciiDigit) ||
            (char.IsAsciiLetterUpper(symbol[^1]) && symbol.Length < 5))
            throw new ArgumentException("Yahoo provider currently supports Taiwan stock and ETF symbols only.", nameof(symbol));
        return symbol + ".TW";
    }

    private async Task<JsonDocument> ChartAsync(string symbol, string query, CancellationToken ct)
    {
        using var response = await http.GetAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?{query}", ct);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        try
        {
            var chart = doc.RootElement.GetProperty("chart");
            if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0 ||
                (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null))
                throw new InvalidDataException("Yahoo returned no chart result.");
            return doc;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            doc.Dispose();
            throw new InvalidDataException("Yahoo chart response is invalid.", ex);
        }
        catch { doc.Dispose(); throw; }
    }

    public Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct) => SnapshotAsync(symbol, Ticker(symbol), ct);
    public Task<MarketSnapshot> GetReferenceSnapshotAsync(string yahooSymbol, CancellationToken ct)
    { MarketReferenceValidation.YahooSymbol(yahooSymbol); return SnapshotAsync(yahooSymbol, yahooSymbol, ct); }
    private async Task<MarketSnapshot> SnapshotAsync(string symbol, string ticker, CancellationToken ct)
    {
        using var doc = await ChartAsync(ticker, "interval=1m&range=1d", ct);
        try
        {
            var chart = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var meta = chart.GetProperty("meta");
            var quote = chart.GetProperty("indicators").GetProperty("quote")[0];
            var opens = quote.GetProperty("open").EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).ToArray();
            decimal open = opens.Length > 0 ? Money(opens[0]) : RequiredDecimal(meta, "regularMarketOpen");
            var marketTime = DateTimeOffset.FromUnixTimeSeconds(meta.GetProperty("regularMarketTime").GetInt64());
            var snapshot = new MarketSnapshot(symbol, RequiredDecimal(meta, "regularMarketPrice"), open,
                RequiredDecimal(meta, "regularMarketDayHigh"), RequiredDecimal(meta, "regularMarketDayLow"),
                RequiredDecimal(meta, "chartPreviousClose"), meta.GetProperty("regularMarketVolume").GetInt64(),
                marketTime, DateTimeOffset.UtcNow);
            if (snapshot.Price <= 0 || snapshot.Open <= 0 || snapshot.High <= 0 || snapshot.Low <= 0 ||
                snapshot.PreviousClose <= 0 || snapshot.Volume < 0 || snapshot.Low > snapshot.High ||
                snapshot.Price < snapshot.Low || snapshot.Price > snapshot.High)
                throw new InvalidDataException("Yahoo chart contains invalid market values.");
            return snapshot;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        { throw new InvalidDataException("Yahoo chart response is invalid.", ex); }
    }

    public Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct) => HistoryAsync(Ticker(symbol), from, through, ct);
    public Task<IReadOnlyList<HistoricalPrice>> GetReferenceHistoricalPricesAsync(string yahooSymbol, DateOnly from, DateOnly through, CancellationToken ct)
    { MarketReferenceValidation.YahooSymbol(yahooSymbol); return HistoryAsync(yahooSymbol, from, through, ct); }
    public async Task<ReferenceHistoryData> GetReferenceHistoryAsync(string yahooSymbol, DateOnly from, DateOnly through, CancellationToken ct)
    {
        MarketReferenceValidation.YahooSymbol(yahooSymbol);
        ValidateHistoryRange(from, through);
        IReadOnlyList<HistoricalPrice> primary = [];
        var attempts = new List<ReferenceHistoryAttempt>();
        string? reason = null;
        try
        {
            primary = await HistoryAsync(yahooSymbol, from, through, ct);
            attempts.Add(new("YAHOO", yahooSymbol, primary.Count, null));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (yahooSymbol == "^TSE50")
        {
            reason = SafeHistoryError("Yahoo", ex);
            attempts.Add(new("YAHOO", yahooSymbol, null, reason));
        }
        // Instrument-specific source resolution belongs in the market adapter, never in an Analyst or Runner.
        // TAI50I is the same price index, not the return index or an ETF proxy.
        if (yahooSymbol == "^TSE50" && primary.Count < 2 && from < through)
        {
            reason ??= $"Yahoo supplied only {primary.Count} daily bars for the requested range.";
            try
            {
                var official = await new TwseTaiwan50History(http).GetAsync(from, through, ct);
                attempts.Add(new("TWSE", "TAI50I", official.Count, null));
                if (official.Count >= 2 && official.Count >= primary.Count)
                    return new(official, new("TWSE", "TAI50I", true, "CLOSE_ONLY", reason + " TWSE provides daily closing price-index values only; open/high/low/volume are unavailable.",
                        from, through, DateTimeOffset.UtcNow, attempts));
                reason += $" TWSE supplied only {official.Count} closing values; insufficient fallback was not selected.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var error = SafeHistoryError("TWSE", ex);
                attempts.Add(new("TWSE", "TAI50I", null, error));
                reason += " " + error;
            }
        }
        return new(primary, new("YAHOO", yahooSymbol, false, primary.Count < 2 ? "INSUFFICIENT" : "OHLCV", reason,
            from, through, DateTimeOffset.UtcNow, attempts));
    }

    private static string SafeHistoryError(string source, Exception ex) =>
        ex is HttpRequestException { StatusCode: { } status } ? $"{source} history returned HTTP {(int)status}." : $"{source} history unavailable ({ex.GetType().Name}).";
    private static void ValidateHistoryRange(DateOnly from, DateOnly through)
    {
        if (from > through || through.DayNumber - from.DayNumber > 366) throw new ArgumentException("Invalid historical date range.");
    }
    private async Task<IReadOnlyList<HistoricalPrice>> HistoryAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct)
    {
        ValidateHistoryRange(from, through);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(through.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        using var doc = await ChartAsync(symbol, $"interval=1d&period1={start}&period2={end}", ct);
        try
        {
            var chart = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var times = chart.GetProperty("timestamp");
            var q = chart.GetProperty("indicators").GetProperty("quote")[0];
            var result = new List<HistoricalPrice>();
            for (var i = 0; i < times.GetArrayLength(); i++)
            {
                var o = q.GetProperty("open")[i]; var h = q.GetProperty("high")[i];
                var l = q.GetProperty("low")[i]; var c = q.GetProperty("close")[i]; var v = q.GetProperty("volume")[i];
                if (o.ValueKind != JsonValueKind.Number || h.ValueKind != JsonValueKind.Number ||
                    l.ValueKind != JsonValueKind.Number || c.ValueKind != JsonValueKind.Number || v.ValueKind != JsonValueKind.Number) continue;
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64()), TaiwanZone).DateTime);
                if (date >= from && date <= through)
                    result.Add(new(date, Money(o), Money(h), Money(l), Money(c), v.GetInt64()));
            }
            return result;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        { throw new InvalidDataException("Yahoo historical response is invalid.", ex); }
    }

    public async Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        using var response = await http.GetAsync($"https://www.twse.com.tw/holidaySchedule/holidaySchedule?response=json&queryYear={date.Year - 1911}", ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        try
        {
            if (doc.RootElement.GetProperty("stat").GetString() != "ok") throw new InvalidDataException("TWSE calendar unavailable.");
            var rows = doc.RootElement.GetProperty("data");
            if (rows.GetArrayLength() == 0) throw new InvalidDataException("TWSE calendar is empty.");
            var isOpen = true;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2 ||
                    !DateOnly.TryParseExact(row[0].GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                    day.Year != date.Year || string.IsNullOrWhiteSpace(row[1].GetString()))
                    throw new InvalidDataException("TWSE calendar contains invalid dates or names.");
                if (day != date) continue;
                var name = row[1].GetString()!;
                isOpen = name.Contains("開始交易日") || name.Contains("最後交易日");
            }
            return isOpen;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        { throw new InvalidDataException("TWSE holiday response is invalid.", ex); }
    }

    private static decimal RequiredDecimal(JsonElement element, string name) => Money(element.GetProperty(name));
    private static decimal Money(JsonElement value) => decimal.Round(value.GetDecimal(), 4);
    private static readonly TimeZoneInfo TaiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
}
