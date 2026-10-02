using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarketSignalAI.Application;
using Microsoft.Extensions.Logging;

namespace MarketSignalAI.Infrastructure;

// TWSE's English JSON interface has stable, unambiguous column labels. All values are EOD.
public sealed class TwseMarketContextProvider(HttpClient http, ILogger<TwseMarketContextProvider> logger) : ITwMarketContextProvider
{
    private const string Base = "https://www.twse.com.tw/en";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new();
    private sealed record CacheEntry(DateTimeOffset StoredAt, JsonElement Body, DateTimeOffset FetchedAt);
    private sealed record Response(JsonElement? Body, DateTimeOffset FetchedAt, string? Error);
    private sealed record TurnoverRow(DateOnly Date, decimal TradeValue);
    private sealed record MarginRow(long MarketValueThousands, long? TargetFinancingUnits,
        long? TargetShortUnits, long MarketPreviousThousands, long? TargetPreviousUnits, long? TargetShortPreviousUnits);
    private sealed record FlowRow(long Foreign, long Trust, long Dealer);

    public async Task<TwMarketContext> GetAsync(string targetSymbol, DateOnly quoteDate, CancellationToken ct)
    {
        // The first three reports cover enough actual trading dates for 20-day calculations,
        // including holiday-heavy months. No synthetic holiday or market row is generated.
        var turnoverRows = new Dictionary<DateOnly, decimal>();
        DateTimeOffset turnoverFetchedAt = DateTimeOffset.UtcNow;
        var turnoverSourceSelected = false;
        var turnoverErrors = new List<string>();
        for (var offset = 0; offset < 3; offset++)
        {
            var month = quoteDate.AddMonths(-offset);
            var response = await FetchAsync($"/exchangeReport/FMTQIK?response=json&date={month:yyyyMM}01", ct);
            if (response.Body is not { } body) { turnoverErrors.Add(response.Error ?? "report unavailable"); continue; }
            try
            {
                var accepted = false;
                foreach (var row in ParseTurnover(body))
                    if (row.Date <= quoteDate) { turnoverRows[row.Date] = row.TradeValue; accepted = true; }
                if (accepted && !turnoverSourceSelected) { turnoverFetchedAt = response.FetchedAt; turnoverSourceSelected = true; }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
            { turnoverErrors.Add(ex.Message); logger.LogWarning("TWSE turnover format failed: {Error}", ex.Message); }
        }
        var dates = turnoverRows.Keys.OrderByDescending(x => x).Take(21).ToArray();
        var turnover = BuildTurnover(turnoverRows, dates, quoteDate, turnoverFetchedAt, turnoverErrors);
        var breadth = await GetBreadthAsync(dates, quoteDate, ct);

        var margins = new Dictionary<DateOnly, MarginRow>();
        var marketFlows = new Dictionary<DateOnly, FlowRow>();
        var targetFlows = new Dictionary<DateOnly, FlowRow>();
        var marginErrors = new List<string>(); var marketErrors = new List<string>(); var targetErrors = new List<string>();
        var marginTimes = new Dictionary<DateOnly, DateTimeOffset>();
        var marketTimes = new Dictionary<DateOnly, DateTimeOffset>();
        var targetTimes = new Dictionary<DateOnly, DateTimeOffset>();
        DateTimeOffset marginFetched = DateTimeOffset.UtcNow, marketFetched = marginFetched, targetFetched = marginFetched;
        foreach (var day in dates)
        {
            var date = day.ToString("yyyyMMdd", Invariant);
            // One margin report supplies both the overall market and the target. Market
            // institutional amounts and target institutional shares are separate reports.
            var requests = await Task.WhenAll(
                FetchAsync($"/exchangeReport/MI_MARGN?response=json&date={date}&selectType=ALL", ct),
                FetchAsync($"/fund/BFI82U?response=json&dayDate={date}&type=day", ct),
                FetchAsync($"/fund/T86?response=json&date={date}&selectType=ALLBUT0999", ct));
            if (day == dates[0]) { marginFetched = requests[0].FetchedAt; marketFetched = requests[1].FetchedAt; targetFetched = requests[2].FetchedAt; }
            TryParse(requests[0], () => ParseMargin(requests[0].Body!.Value, targetSymbol), margins, day, marginErrors, "margin");
            TryParse(requests[1], () => ParseMarketFlow(requests[1].Body!.Value), marketFlows, day, marketErrors, "market institutional flow");
            TryParse(requests[2], () => ParseTargetFlow(requests[2].Body!.Value, targetSymbol), targetFlows, day, targetErrors, "target institutional flow");
            if (margins.ContainsKey(day)) marginTimes[day] = requests[0].FetchedAt;
            if (marketFlows.ContainsKey(day)) marketTimes[day] = requests[1].FetchedAt;
            if (targetFlows.ContainsKey(day)) targetTimes[day] = requests[2].FetchedAt;
        }
        var margin = new TwMarginContext(
            BuildMargin(margins, dates, quoteDate, marginFetched, marginTimes, marginErrors, "MARKET", "NTD_thousands", x => x.MarketValueThousands, x => x.MarketPreviousThousands),
            BuildMargin(margins, dates, quoteDate, marginFetched, marginTimes, marginErrors, "TARGET", "TWSE_trading_units", x => x.TargetFinancingUnits, x => x.TargetPreviousUnits),
            BuildMargin(margins, dates, quoteDate, marginFetched, marginTimes, marginErrors, "TARGET", "TWSE_trading_units", x => x.TargetShortUnits, x => x.TargetShortPreviousUnits));
        var flows = new TwInstitutionFlow(
            BuildFlow(marketFlows, dates, quoteDate, marketFetched, marketTimes, marketErrors, "MARKET", "NTD", x => x.Foreign),
            BuildFlow(marketFlows, dates, quoteDate, marketFetched, marketTimes, marketErrors, "MARKET", "NTD", x => x.Trust),
            BuildFlow(marketFlows, dates, quoteDate, marketFetched, marketTimes, marketErrors, "MARKET", "NTD", x => x.Dealer),
            BuildFlow(targetFlows, dates, quoteDate, targetFetched, targetTimes, targetErrors, "TARGET", "shares", x => x.Foreign),
            BuildFlow(targetFlows, dates, quoteDate, targetFetched, targetTimes, targetErrors, "TARGET", "shares", x => x.Trust),
            BuildFlow(targetFlows, dates, quoteDate, targetFetched, targetTimes, targetErrors, "TARGET", "shares", x => x.Dealer));
        return new(turnover, breadth, margin, flows);
    }

    private void TryParse<T>(Response response, Func<T> parser, Dictionary<DateOnly, T> output,
        DateOnly date, List<string> errors, string source)
    {
        if (response.Body is null) { errors.Add(response.Error ?? "report unavailable"); return; }
        if (!response.Body.Value.TryGetProperty("date", out var actual) || actual.GetString() != date.ToString("yyyyMMdd", Invariant))
        { errors.Add($"TWSE report date mismatch for {date}."); return; }
        try { output[date] = parser(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { errors.Add(ex.Message); logger.LogWarning("TWSE {Source} format failed for {Date}: {Error}", source, date, ex.Message); }
    }

    private async Task<Response> FetchAsync(string path, CancellationToken ct)
    {
        if (cache.TryGetValue(path, out var cached) && DateTimeOffset.UtcNow - cached.StoredAt < TimeSpan.FromMinutes(15))
            return new(cached.Body, cached.FetchedAt, null);
        var fetchedAt = DateTimeOffset.UtcNow;
        try
        {
            using var response = await http.GetAsync(Base + path, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new(null, fetchedAt, $"TWSE HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK")
                return new(null, fetchedAt, $"TWSE report not published: {(root.TryGetProperty("stat", out var unavailable) ? unavailable.GetString() : "missing status")}");
            cache[path] = new(DateTimeOffset.UtcNow, root, fetchedAt);
            return new(root, fetchedAt, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        { logger.LogWarning("TWSE request failed {Path}: {Message}", path, ex.Message); return new(null, fetchedAt, $"TWSE request failed: {ex.Message}"); }
    }

    private static IEnumerable<TurnoverRow> ParseTurnover(JsonElement root)
    {
        RequireFields(root.GetProperty("fields"), "Date", "Trade Volume", "Trade Value");
        foreach (var row in root.GetProperty("data").EnumerateArray())
        {
            if (row.GetArrayLength() < 3) continue;
            yield return new(DateOnly.ParseExact(row[0].GetString()!, "yyyy/MM/dd", Invariant), Decimal(row[2]));
        }
    }
    private static TwTurnover BuildTurnover(Dictionary<DateOnly, decimal> values, DateOnly[] dates,
        DateOnly quoteDate, DateTimeOffset fetchedAt, List<string> errors)
    {
        const string source = "TWSE /exchangeReport/FMTQIK";
        if (dates.Length == 0) return new(new(source, null, fetchedAt, "UNAVAILABLE", "EOD", FirstError(errors)),
            "NTD", null, null, null, null, null);
        var current = values[dates[0]];
        decimal? Average(int days) => dates.Length >= days ? decimal.Round(dates.Take(days).Average(x => values[x]), 2) : null;
        var avg5 = Average(5); var avg20 = Average(20);
        decimal? Versus(decimal? average) => average is > 0 ? decimal.Round((current / average.Value - 1) * 100, 4) : null;
        return new(new(source, dates[0], fetchedAt, Status(dates[0], quoteDate, avg20.HasValue), "EOD",
            Reason(dates[0], quoteDate, avg20.HasValue, errors)), "NTD", current, avg5, avg20, Versus(avg5), Versus(avg20));
    }

    private async Task<TwBreadth> GetBreadthAsync(DateOnly[] dates, DateOnly quoteDate, CancellationToken ct)
    {
        const string source = "TWSE /exchangeReport/MI_INDEX";
        var attempted = new List<string>();
        foreach (var day in dates.Take(5))
        {
            var result = await FetchAsync($"/exchangeReport/MI_INDEX?response=json&date={day:yyyyMMdd}&type=ALL", ct);
            if (result.Body is not { } body) { attempted.Add(result.Error ?? "report unavailable"); continue; }
            try
            {
                if (!body.TryGetProperty("date", out var actual) || actual.GetString() != day.ToString("yyyyMMdd", Invariant))
                    throw new InvalidDataException("TWSE breadth report date mismatch.");
                var tables = body.GetProperty("tables").EnumerateArray();
                var table = tables.Single(x => x.TryGetProperty("title", out var title) &&
                    title.GetString() == "Net Change of Price (Number of Listed Securities)");
                RequireFields(table.GetProperty("fields"), "Type", "Overall Market", "Stocks");
                var rows = table.GetProperty("data").EnumerateArray().ToDictionary(x => x[0].GetString()!.Trim(), StringComparer.OrdinalIgnoreCase);
                string StockCount(string label)
                {
                    if (!rows.TryGetValue(label, out var row) || row.GetArrayLength() < 3)
                        throw new InvalidDataException($"TWSE breadth row '{label}' missing; available: {string.Join(", ", rows.Keys)}.");
                    return row[2].GetString() ?? throw new InvalidDataException($"TWSE breadth {label} stock count missing.");
                }
                var up = StockCount("Up (Limit Up)");
                var down = StockCount("Down (Limit Down)");
                var unchanged = StockCount("Unchanged");
                return new(new(source, day, result.FetchedAt, Status(day, quoteDate, true), "EOD",
                    Reason(day, quoteDate, true, attempted)), "TWSE listed stocks",
                    LeadingCount(up), LeadingCount(down), LeadingCount(unchanged), ParentheticalCount(up), ParentheticalCount(down));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { attempted.Add(ex.Message); logger.LogWarning("TWSE breadth format failed for {Date}: {Error}", day, ex.Message); }
        }
        return new(new(source, null, DateTimeOffset.UtcNow, "UNAVAILABLE", "EOD", FirstError(attempted)),
            "TWSE listed stocks", null, null, null, null, null);
    }
    private static int LeadingCount(string value)
    {
        var match = Regex.Match(value, @"^\s*([\d,]+)");
        if (!match.Success) throw new FormatException("TWSE breadth count is malformed.");
        return int.Parse(match.Groups[1].Value, NumberStyles.AllowThousands, Invariant);
    }
    private static int? ParentheticalCount(string value)
    {
        var match = Regex.Match(value, @"\(([\d,]+)\)");
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.AllowThousands, Invariant) : null;
    }

    private static MarginRow ParseMargin(JsonElement root, string symbol)
    {
        var tables = root.GetProperty("tables").EnumerateArray();
        var summary = tables.Single(x => (x.GetProperty("title").GetString() ?? "").Contains("Margin transaction summary", StringComparison.Ordinal));
        var details = tables.Single(x => (x.GetProperty("title").GetString() ?? "").Contains("Margin Transactions (All)", StringComparison.Ordinal));
        RequireFields(summary.GetProperty("fields"), "Item", "Margin Purchase/ Short Covering");
        RequireFields(details.GetProperty("fields"), "Security Code", "Margin Purchase");
        var market = summary.GetProperty("data").EnumerateArray().Single(x => x[0].GetString() == "Margin Purchase Value (In thousands)");
        var target = details.GetProperty("data").EnumerateArray().FirstOrDefault(x => x[0].GetString() == symbol);
        if (market.GetArrayLength() < 6 || target.ValueKind == JsonValueKind.Array && target.GetArrayLength() < 12) throw new InvalidDataException("TWSE margin columns are missing.");
        return new(Long(market[5]), target.ValueKind == JsonValueKind.Array ? Long(target[5]) : null,
            target.ValueKind == JsonValueKind.Array ? Long(target[11]) : null, Long(market[4]),
            target.ValueKind == JsonValueKind.Array ? Long(target[4]) : null,
            target.ValueKind == JsonValueKind.Array ? Long(target[10]) : null);
    }
    private static FlowRow ParseMarketFlow(JsonElement root)
    {
        RequireFields(root.GetProperty("fields"), "Item", "Total Buy", "Total Sell", "Difference");
        var rows = root.GetProperty("data").EnumerateArray().ToDictionary(x => x[0].GetString()!);
        return new(Long(rows["Foreign Investors include Mainland Area Investors(Foreign Dealers excluded)"][3]),
            Long(rows["Securities Investment Trust Companies"][3]),
            checked(Long(rows["Dealers (Proprietary)"][3]) + Long(rows["Dealers (Hedge)"][3])));
    }
    private static FlowRow ParseTargetFlow(JsonElement root, string symbol)
    {
        var fields = root.GetProperty("fields");
        if (fields.GetArrayLength() != 18 || fields[0].GetString() != "Security Code" || fields[17].GetString() != "Total Difference")
            throw new InvalidDataException("TWSE target institutional columns changed.");
        var row = root.GetProperty("data").EnumerateArray().FirstOrDefault(x => x[0].GetString() == symbol);
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 18)
            throw new KeyNotFoundException($"TWSE institutional report has no target row for {symbol}.");
        return new(Long(row[3]), Long(row[9]), Long(row[10]));
    }

    private static TwMarginSeries BuildMargin(Dictionary<DateOnly, MarginRow> rows, DateOnly[] dates,
        DateOnly quoteDate, DateTimeOffset fetchedAt, IReadOnlyDictionary<DateOnly, DateTimeOffset> fetchedTimes, List<string> errors, string scope, string unit,
        Func<MarginRow, long?> value, Func<MarginRow, long?> previous)
    {
        var source = $"TWSE /exchangeReport/MI_MARGN {scope}";
        var offset = Array.FindIndex(dates, d => rows.TryGetValue(d, out var row) && value(row).HasValue);
        if (offset < 0) return new(new(source, null, fetchedAt, "UNAVAILABLE", "EOD",
            scope == "TARGET" ? "TWSE margin report has no target row for this security." : FirstError(errors)), unit, null, null, null, null);
        var day = dates[offset]; var current = value(rows[day])!.Value;
        long? Change(int tradingDays) => offset + tradingDays < dates.Length && rows.TryGetValue(dates[offset + tradingDays], out var old)
            && value(old) is { } oldValue ? checked(current - oldValue) : null;
        var c5 = Change(5); var c20 = Change(20);
        return new(new(source, day, fetchedTimes[day], Status(day, quoteDate, c20.HasValue), "EOD",
            Reason(day, quoteDate, c20.HasValue, errors)), unit, current, previous(rows[day]) is { } prior ? checked(current - prior) : null, c5, c20);
    }
    private static TwInstitutionSeries BuildFlow(Dictionary<DateOnly, FlowRow> rows, DateOnly[] dates,
        DateOnly quoteDate, DateTimeOffset fetchedAt, IReadOnlyDictionary<DateOnly, DateTimeOffset> fetchedTimes, List<string> errors, string scope, string unit,
        Func<FlowRow, long> value)
    {
        var source = scope == "MARKET" ? "TWSE /fund/BFI82U MARKET" : "TWSE /fund/T86 TARGET";
        var offset = Array.FindIndex(dates, rows.ContainsKey);
        if (offset < 0) return new(new(source, null, fetchedAt, "UNAVAILABLE", "EOD", FirstError(errors)), unit, null, null, null);
        var day = dates[offset];
        long? Sum(int tradingDays)
        {
            if (offset + tradingDays > dates.Length) return null;
            long total = 0;
            for (var i = offset; i < offset + tradingDays; i++)
            {
                if (!rows.TryGetValue(dates[i], out var row)) return null;
                total = checked(total + value(row));
            }
            return total;
        }
        var c5 = Sum(5); var c20 = Sum(20);
        return new(new(source, day, fetchedTimes[day], Status(day, quoteDate, c20.HasValue), "EOD",
            Reason(day, quoteDate, c20.HasValue, errors)), unit, value(rows[day]), c5, c20);
    }

    private static string Status(DateOnly observed, DateOnly quote, bool complete) =>
        observed < quote ? "STALE" : complete ? "AVAILABLE" : "PARTIAL";
    private static string? Reason(DateOnly observed, DateOnly quote, bool complete, List<string> errors) =>
        observed < quote ? $"Last published date {observed} precedes target quote date {quote}. Never describe this as today's data." :
        !complete ? "20 trading-day history is incomplete; unavailable derived metrics are null." :
        errors.Count > 0 ? $"Some older reports failed: {FirstError(errors)}" : null;
    private static string FirstError(List<string> errors) => errors.Count == 0 ? "TWSE has no published usable report for the requested dates." : errors[0];
    private static void RequireFields(JsonElement fields, params string[] expected)
    {
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() < expected.Length)
            throw new InvalidDataException("TWSE report fields are missing.");
        for (var i = 0; i < expected.Length; i++)
            if (fields[i].GetString() != expected[i]) throw new InvalidDataException($"TWSE field {i} changed.");
    }
    private static decimal Decimal(JsonElement value) => decimal.Parse(value.GetString()!.Replace(",", "", StringComparison.Ordinal),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant);
    private static long Long(JsonElement value) => long.Parse(value.GetString()!.Replace(",", "", StringComparison.Ordinal),
        NumberStyles.AllowLeadingSign, Invariant);
}
