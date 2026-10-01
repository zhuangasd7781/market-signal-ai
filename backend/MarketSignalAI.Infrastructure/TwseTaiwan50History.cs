using System.Globalization;
using System.Text.Json;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

// Official monthly Taiwan50 price-index closes. No synthetic OHLC or volume.
internal sealed class TwseTaiwan50History(HttpClient http)
{
    public async Task<IReadOnlyList<HistoricalPrice>> GetAsync(DateOnly from, DateOnly through, CancellationToken ct)
    {
        var prices = new SortedDictionary<DateOnly, HistoricalPrice>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= through; month = month.AddMonths(1))
        {
            using var response = await http.GetAsync($"https://www.twse.com.tw/indicesReport/TAI50I?date={month:yyyyMMdd}&response=json", ct);
            response.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (!root.TryGetProperty("stat", out var stat) || !string.Equals(stat.GetString(), "OK", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("title", out var title) || title.GetString() != "臺灣50指數歷史資料" ||
                !root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() < 2 ||
                fields[0].GetString() != "日期" || fields[1].GetString() != "臺灣50指數" ||
                !root.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("TWSE Taiwan50 report identity or fields are invalid.");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2 ||
                    row[0].ValueKind != JsonValueKind.String || row[1].ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("TWSE Taiwan50 row is invalid.");
                var parts = (row[0].GetString() ?? "").Split('/');
                if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rocYear) ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rowMonth) ||
                    !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day) || rocYear is < 1 or > 999 ||
                    !decimal.TryParse(row[1].GetString(), NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var close) || close <= 0)
                    throw new InvalidDataException("TWSE Taiwan50 date or closing value is invalid.");
                DateOnly date;
                try { date = new(rocYear + 1911, rowMonth, day); }
                catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("TWSE Taiwan50 date is invalid.", ex); }
                if (date.Year != month.Year || date.Month != month.Month)
                    throw new InvalidDataException("TWSE Taiwan50 report does not match the requested month.");
                if (date < from || date > through) continue;
                if (!prices.TryAdd(date, new(date, null, null, null, close, null)))
                    throw new InvalidDataException("TWSE Taiwan50 report contains duplicate dates.");
            }
        }
        return prices.Values.ToArray();
    }
}
