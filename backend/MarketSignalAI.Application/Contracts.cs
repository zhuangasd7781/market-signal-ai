using MarketSignalAI.Domain;

namespace MarketSignalAI.Application;

public interface ICurrentUser { long UserId { get; } }

public interface ISignalStore
{
    Task<User?> GetUserAsync(long userId, CancellationToken ct);
    Task<IReadOnlyList<AIProvider>> GetProvidersAsync(CancellationToken ct);
    Task<IReadOnlyList<Product>> SearchProductsAsync(string query, CancellationToken ct);
    Task<Product?> GetProductAsync(string symbol, string market, CancellationToken ct);
    Task<IReadOnlyList<Product>> GetWatchlistAsync(long userId, CancellationToken ct);
    Task AddWatchAsync(long userId, long productId, CancellationToken ct);
    Task RemoveWatchAsync(long userId, long productId, CancellationToken ct);
    Task<UserPosition?> GetPositionAsync(long userId, long productId, CancellationToken ct);
    Task SavePositionAsync(UserPosition position, CancellationToken ct);
    Task<IReadOnlyList<AnalysisRecord>> GetHistoryAsync(long userId, long? productId, CancellationToken ct);
    Task<bool> IsHealthyAsync(CancellationToken ct);
}

public sealed record Signal(string Provider, string DisplayName, string Action, decimal? Quantity,
    bool Changed, DateTime AnalyzedAt);
public sealed record WatchlistRow(Product Product, IReadOnlyList<Signal> Signals, DateTime? LastAnalyzedAt);
public sealed record WatchlistResponse(IReadOnlyList<AIProvider> Providers, IReadOnlyList<WatchlistRow> Items, bool IsMock = true);
public sealed record AnalysisView(long Id, string Provider, string DisplayName, string Model, Analysis Result, DateTime CreatedAt, TokenUsage? Usage = null, string? PromptVersion = null, AnalysisContextView? Context = null);

public sealed class SignalService(ISignalStore store, ICurrentUser currentUser)
{
    public async Task<WatchlistResponse> GetWatchlistAsync(CancellationToken ct)
    {
        var providers = await store.GetProvidersAsync(ct);
        var products = await store.GetWatchlistAsync(currentUser.UserId, ct);
        var history = await store.GetHistoryAsync(currentUser.UserId, null, ct);
        var groups = history.ToLookup(x => (x.ProductId, x.AIProviderId));
        var rows = products.Select(product =>
        {
            var signals = new List<Signal>();
            foreach (var provider in providers)
            {
                var pair = groups[(product.Id, provider.Id)].OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id).Take(2).ToArray();
                if (pair.Length == 0) continue;
                var latest = pair[0];
                var changed = pair.Length == 2 && (latest.Result.Action != pair[1].Result.Action || latest.Result.Quantity != pair[1].Result.Quantity);
                signals.Add(new(provider.Code, provider.DisplayName, latest.Result.Action, latest.Result.Quantity, changed, latest.CreatedAt));
            }
            return new WatchlistRow(product, signals, signals.Count == 0 ? null : signals.Max(x => x.AnalyzedAt));
        }).ToArray();
        return new(providers, rows);
    }

    public async Task<Product> RequireProductAsync(string symbol, string market, CancellationToken ct) =>
        await store.GetProductAsync(symbol, market, ct) ?? throw new KeyNotFoundException("找不到商品。");

    public async Task<IReadOnlyList<AnalysisView>> GetAnalysisAsync(long productId, bool history, CancellationToken ct)
    {
        var providers = await store.GetProvidersAsync(ct);
        var records = await store.GetHistoryAsync(currentUser.UserId, productId, ct);
        var sorted = records.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
        var selected = history ? sorted.AsEnumerable() : sorted.DistinctBy(x => x.AIProviderId);
        return selected.Where(x => providers.Any(p => p.Id == x.AIProviderId)).Select(x =>
        {
            var provider = providers.Single(p => p.Id == x.AIProviderId);
            var trace = AnalysisContextProjection.Read(x.InputSnapshotJson);
            return new AnalysisView(x.Id, provider.Code, provider.DisplayName, x.Model, x.Result, x.CreatedAt, x.Usage, trace.Version, trace.Context);
        }).ToArray();
    }
}
