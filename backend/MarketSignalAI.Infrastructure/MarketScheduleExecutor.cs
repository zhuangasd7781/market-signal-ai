using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;

namespace MarketSignalAI.Infrastructure;

public sealed class MarketScheduleExecutor(IMarketDataProvider market, IMarketStore store,
    IMarketAnalysisRunner runner, TimeProvider clock, ILogger<MarketScheduleExecutor> logger) : IMarketScheduleExecutor
{
    public async Task ExecuteAsync(DateOnly date, bool isOpeningCheck, CancellationToken ct)
    {
        logger.LogInformation("Schedule started {TradeDate} {OpeningCheck}", date, isOpeningCheck);
        var day = await store.GetTradingDayAsync(date, "TW", ct);
        if (isOpeningCheck || day is null)
        {
            var isOpen = await market.IsTradingDayAsync(date, ct);
            day = new TradingDay(date, "TW", isOpen ? "OPEN" : "CLOSED", clock.GetUtcNow());
            await store.SaveTradingDayAsync(day, ct);
            logger.LogInformation("Market {MarketStatus} {TradeDate}", day.Status, date);
        }
        if (!isOpeningCheck && day.Status == "OPEN")
        {
            var batch = await runner.RunAllAsync(ct, date);
            logger.LogInformation("Schedule completed {TradeDate} {Completed} {Failed}", date, batch.Completed.Count, batch.Failed.Count);
        }
        else logger.LogInformation("Schedule completed {TradeDate} {MarketStatus}", date, day.Status);
    }
}
