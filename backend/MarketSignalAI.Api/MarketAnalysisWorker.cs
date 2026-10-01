using MarketSignalAI.Application;

namespace MarketSignalAI.Api;

public sealed class MarketAnalysisWorker(IServiceScopeFactory scopes, ILogger<MarketAnalysisWorker> logger, TimeProvider clock) : BackgroundService
{
    private static readonly TimeZoneInfo TaiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    private static readonly HashSet<int> AnalysisMinutes = [9 * 60 + 5, 10 * 60 + 5, 12 * 60 + 5, 13 * 60 + 5];
    private readonly HashSet<(DateOnly, int)> completed = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20), clock);
        do
        {
            var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TaiwanZone);
            var date = DateOnly.FromDateTime(now.DateTime);
            var minute = now.Hour * 60 + now.Minute;
            if ((minute == 8 * 60 + 30 || AnalysisMinutes.Contains(minute)) && !completed.Contains((date, minute)))
            {
                try
                {
                    await TriggerAsync(date, minute, stoppingToken);
                    completed.Add((date, minute));
                    completed.RemoveWhere(x => x.Item1 < date);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Schedule failed {TradeDate} {Minute}", date, minute); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TriggerAsync(DateOnly date, int minute, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMarketScheduleExecutor>()
            .ExecuteAsync(date, minute == 8 * 60 + 30, ct);
    }
}
