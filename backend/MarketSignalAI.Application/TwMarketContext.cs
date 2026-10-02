namespace MarketSignalAI.Application;

// Each section is dated independently. These are official end-of-day observations, not live quotes.
public sealed record TwEvidence(string Source, DateOnly? MarketDate, DateTimeOffset FetchedAt,
    string Status, string? DataQuality = null, string? Reason = null);
public sealed record TwTurnover(TwEvidence Evidence, string Unit, decimal? Current, decimal? Average5D,
    decimal? Average20D, decimal? VsAverage5DPercent, decimal? VsAverage20DPercent);
public sealed record TwBreadth(TwEvidence Evidence, string Universe, int? Advancers, int? Decliners,
    int? Unchanged, int? LimitUp, int? LimitDown);
public sealed record TwMarginSeries(TwEvidence Evidence, string Unit, long? Current,
    long? Change1D, long? Change5D, long? Change20D);
public sealed record TwMarginContext(TwMarginSeries MarketFinancing, TwMarginSeries TargetFinancing,
    TwMarginSeries TargetShortSelling);
public sealed record TwInstitutionSeries(TwEvidence Evidence, string Unit, long? Today,
    long? Cumulative5D, long? Cumulative20D);
public sealed record TwInstitutionFlow(TwInstitutionSeries MarketForeign, TwInstitutionSeries MarketInvestmentTrust,
    TwInstitutionSeries MarketDealer, TwInstitutionSeries TargetForeign,
    TwInstitutionSeries TargetInvestmentTrust, TwInstitutionSeries TargetDealer);
public sealed record TwMarketContext(TwTurnover Turnover, TwBreadth Breadth,
    TwMarginContext Margin, TwInstitutionFlow InstitutionalFlow)
{
    public static TwMarketContext Unavailable(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        TwEvidence Evidence(string source) => new(source, null, now, "UNAVAILABLE", "EOD", reason);
        TwMarginSeries Margin(string scope, string unit) => new(Evidence("TWSE /exchangeReport/MI_MARGN " + scope), unit, null, null, null, null);
        TwInstitutionSeries Flow(string scope, string unit) => new(Evidence(scope == "MARKET" ? "TWSE /fund/BFI82U MARKET" : "TWSE /fund/T86 TARGET"), unit, null, null, null);
        return new(new(Evidence("TWSE /exchangeReport/FMTQIK"), "NTD", null, null, null, null, null),
            new(Evidence("TWSE /exchangeReport/MI_INDEX"), "TWSE listed stocks", null, null, null, null, null),
            new(Margin("MARKET", "NTD_thousands"), Margin("TARGET", "TWSE_trading_units"), Margin("TARGET", "TWSE_trading_units")),
            new(Flow("MARKET", "NTD"), Flow("MARKET", "NTD"), Flow("MARKET", "NTD"),
                Flow("TARGET", "shares"), Flow("TARGET", "shares"), Flow("TARGET", "shares")));
    }
}
public sealed record TargetReturns(DateTimeOffset QuoteTime, decimal? Return5DPercent, decimal? Return20DPercent);

public interface ITwMarketContextProvider
{
    Task<TwMarketContext> GetAsync(string targetSymbol, DateOnly quoteDate, CancellationToken ct);
}

public static class TargetReturnsCalculator
{
    public static TargetReturns Calculate(MarketSignalAI.Domain.MarketSnapshot snapshot,
        IReadOnlyList<MarketSignalAI.Domain.HistoricalPrice> history)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.MarketTime,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei")).DateTime);
        var previous = history.Where(x => x.TradeDate < date && x.Close > 0)
            .OrderByDescending(x => x.TradeDate).ToArray();
        decimal? Return(int tradingDays) => previous.Length >= tradingDays
            ? decimal.Round((snapshot.Price / previous[tradingDays - 1].Close - 1) * 100, 4) : null;
        return new(snapshot.MarketTime, Return(5), Return(20));
    }
}
