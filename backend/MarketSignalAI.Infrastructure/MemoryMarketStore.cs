using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public sealed class MemoryMarketStore(MemorySignalStore signals) : IMarketStore
{
    private readonly object gate = new();
    private readonly List<StoredMarketSnapshot> snapshots = [];
    private readonly List<ProviderFailure> failures = [];
    public IReadOnlyList<ProviderFailure> Failures { get { lock (gate) return failures.ToArray(); } }
    private readonly Dictionary<(DateOnly, string), TradingDay> days = [];
    public Task<StoredMarketSnapshot?> GetLatestSnapshotAsync(long productId, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(snapshots.LastOrDefault(x => x.ProductId == productId));
    }
    public Task<StoredMarketSnapshot> SaveSnapshotAsync(long productId, MarketSnapshot snapshot, CancellationToken ct)
    {
        lock (gate) { var result = new StoredMarketSnapshot(snapshots.Count + 1, productId, snapshot); snapshots.Add(result); return Task.FromResult(result); }
    }
    public Task<TradingDay?> GetTradingDayAsync(DateOnly date, string market, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(days.GetValueOrDefault((date, market)));
    }
    public Task SaveTradingDayAsync(TradingDay day, CancellationToken ct)
    {
        lock (gate) days[(day.TradeDate, day.Market)] = day;
        return Task.CompletedTask;
    }
    public async Task<IReadOnlyList<Product>> GetActiveTrackedProductsAsync(string market, CancellationToken ct) =>
        (await signals.GetWatchlistAsync(1, ct)).Where(x => x.Market == market).ToArray();
    public Task<IReadOnlyList<long>> GetTrackingUserIdsAsync(long productId, CancellationToken ct) => signals.GetTrackingUserIdsAsync(productId, ct);
    public Task SaveProviderFailureAsync(ProviderFailure failure, CancellationToken ct)
    {
        lock (gate) failures.Add(failure);
        return Task.CompletedTask;
    }
    public Task<AnalysisRecord> SaveAnalysisAsync(AnalysisRecord result, CancellationToken ct) => Task.FromResult(signals.AddAnalysis(result));
}
