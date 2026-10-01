using MarketSignalAI.Application;
namespace MarketSignalAI.Api;
public sealed class MarketAnalysisWorker(IServiceScopeFactory scopes, ILogger<MarketAnalysisWorker> logger, TimeProvider clock) : BackgroundService
{
    private static readonly TimeZoneInfo TaiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
    private readonly MarketSignalAI.Infrastructure.MemoryAnalysisScheduleStore legacySettings = new();
    private readonly HashSet<DateOnly> openingChecks = [];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(20),clock);
        do
        {
            try { await RunDueAsync(stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Schedule failed");}
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    public async Task RunDueAsync(CancellationToken ct)
    {
        var now=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TaiwanZone);
        var date=DateOnly.FromDateTime(now.DateTime);
        var minute=now.Hour*60+now.Minute;
        using var scope=scopes.CreateScope();
        var executor=scope.ServiceProvider.GetRequiredService<IMarketScheduleExecutor>();
        // Calendar check is independent of user analysis settings, including an empty time list.
        if(minute==8*60+30 && !openingChecks.Contains(date))
        {
            await executor.ExecuteAsync(date,true,ct);
            openingChecks.Add(date); openingChecks.RemoveWhere(d=>d<date);
        }
        var settings=scope.ServiceProvider.GetService<IAnalysisScheduleStore>() ?? legacySettings;
        var userId=scope.ServiceProvider.GetService<ICurrentUser>()?.UserId ?? 1;
        var schedule=await settings.GetAsync(userId,ct);
        if(!schedule.Enabled || !schedule.Times.Any(t=>AnalysisSchedule.Minute(t)==minute))return;
        // Persist claim before executing. An interrupted/failed paid run requires manual force;
        // automatically retrying could duplicate a provider call already accepted upstream.
        if(!await settings.TryClaimAsync(userId,date,minute,ct))return;
        await executor.ExecuteAsync(date,false,ct);
    }
}
