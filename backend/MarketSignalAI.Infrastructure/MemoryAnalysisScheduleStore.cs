using MarketSignalAI.Application;
namespace MarketSignalAI.Infrastructure;
public sealed class MemoryAnalysisScheduleStore : IAnalysisScheduleStore
{
    private readonly object gate = new();
    private readonly Dictionary<long, AnalysisSchedule> settings = [];
    private readonly HashSet<(long, DateOnly, int)> claims = [];
    public Task<AnalysisSchedule> GetAsync(long userId, CancellationToken ct) { lock(gate) return Task.FromResult(settings.GetValueOrDefault(userId, AnalysisSchedule.Default)); }
    public Task SaveAsync(long userId, AnalysisSchedule schedule, CancellationToken ct) { schedule.Validate(); lock(gate) settings[userId] = schedule with { Times = schedule.Times.Order().ToArray() }; return Task.CompletedTask; }
    public Task<bool> TryClaimAsync(long userId, DateOnly date, int minute, CancellationToken ct) { lock(gate) return Task.FromResult(claims.Add((userId,date,minute))); }
}
