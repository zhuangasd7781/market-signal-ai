using MarketSignalAI.Domain;

namespace MarketSignalAI.Application;

public interface IMarketDataProvider
{
    Task<MarketSnapshot> GetSnapshotAsync(string symbol, CancellationToken ct);
    Task<IReadOnlyList<HistoricalPrice>> GetHistoricalPricesAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct);
    Task<MarketSnapshot> GetReferenceSnapshotAsync(string yahooSymbol, CancellationToken ct) => throw new NotSupportedException("Reference quotes are not supported by this adapter.");
    Task<IReadOnlyList<HistoricalPrice>> GetReferenceHistoricalPricesAsync(string yahooSymbol, DateOnly from, DateOnly through, CancellationToken ct) => throw new NotSupportedException("Reference history is not supported by this adapter.");
    async Task<ReferenceHistoryData> GetReferenceHistoryAsync(string symbol, DateOnly from, DateOnly through, CancellationToken ct)
    {
        var prices = await GetReferenceHistoricalPricesAsync(symbol, from, through, ct);
        return new(prices, new("UNSPECIFIED", symbol, false, prices.Count < 2 ? "INSUFFICIENT" : "OHLCV",
            null, from, through, DateTimeOffset.UtcNow, []));
    }
    Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct);
}

public interface IMarketStore
{
    Task<StoredMarketSnapshot?> GetLatestSnapshotAsync(long productId, CancellationToken ct);
    Task<StoredMarketSnapshot> SaveSnapshotAsync(long productId, MarketSnapshot snapshot, CancellationToken ct);
    Task<TradingDay?> GetTradingDayAsync(DateOnly date, string market, CancellationToken ct);
    Task SaveTradingDayAsync(TradingDay day, CancellationToken ct);
    Task<IReadOnlyList<Product>> GetActiveTrackedProductsAsync(string market, CancellationToken ct);
    Task<IReadOnlyList<long>> GetTrackingUserIdsAsync(long productId, CancellationToken ct);
    Task<AnalysisRecord> SaveAnalysisAsync(AnalysisRecord result, CancellationToken ct);
    Task SaveProviderFailureAsync(ProviderFailure failure, CancellationToken ct);
}

public sealed record MarketContext(Product Product, MarketSnapshot Snapshot, IReadOnlyList<HistoricalPrice> History,
    UserPosition? Position, Analysis? PreviousDecision)
{
    public IReadOnlyList<MarketReferenceContext> MarketReferences { get; init; } = [];
    public IReadOnlyList<PreviousProviderDecision> PreviousDecisions { get; init; } = [];
}
public sealed record AnalystResult(string Model, Analysis Analysis, string RawResponse, string? Instructions = null, TokenUsage? Usage = null, string? ReasoningEffort = null);
public interface IAIAnalyst
{
    string ProviderCode { get; }
    string? Model => null;
    string? ReasoningEffort => null;
    TimeSpan Timeout => TimeSpan.FromSeconds(180);
    Task<AnalystResult> AnalyzeAsync(MarketContext context, CancellationToken ct);
}
public sealed record ProviderRunResult(string Provider, string Status, long? AnalysisId, string? Error,
    string? Model, Analysis? Result, TokenUsage? Usage = null);
public sealed record ProductRunResult(string Symbol, long SnapshotId, IReadOnlyList<ProviderRunResult> Providers);
public sealed record ProductRunFailure(string Symbol, string Error);
public sealed record BatchRunResult(IReadOnlyList<ProductRunResult> Completed, IReadOnlyList<ProductRunFailure> Failed);
public interface IMarketAnalysisRunner
{
    Task<ProductRunResult> RunProductAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate = null);
    Task<BatchRunResult> RunAllAsync(CancellationToken ct, DateOnly? expectedTradeDate = null);
}
public interface IMarketScheduleExecutor
{
    Task ExecuteAsync(DateOnly date, bool isOpeningCheck, CancellationToken ct);
}
