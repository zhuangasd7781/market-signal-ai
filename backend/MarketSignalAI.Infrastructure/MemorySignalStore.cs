using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public sealed class MemorySignalStore : ISignalStore
{
    private readonly object gate = new();
    private readonly HashSet<(long User, long Product)> watchlist = [(1, 1), (1, 2), (1, 3)];
    private readonly Dictionary<(long, long), UserPosition> positions = new();
    private readonly AnalysisRecord[] history = DemoData.History(DateTime.UtcNow);
    public Task<User?> GetUserAsync(long userId, CancellationToken ct) => Task.FromResult(userId == 1 ? DemoData.User : null);
    public Task<IReadOnlyList<AIProvider>> GetProvidersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AIProvider>>(DemoData.Providers);
    public Task<IReadOnlyList<Product>> SearchProductsAsync(string query, CancellationToken ct) => Task.FromResult<IReadOnlyList<Product>>(
        DemoData.Products.Where(x => x.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
    public Task<Product?> GetProductAsync(string symbol, string market, CancellationToken ct) => Task.FromResult(
        DemoData.Products.SingleOrDefault(x => x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && x.Market.Equals(market, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<Product>> GetWatchlistAsync(long userId, CancellationToken ct)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<Product>>(DemoData.Products.Where(x => watchlist.Contains((userId, x.Id))).ToArray());
    }
    public Task AddWatchAsync(long userId, long productId, CancellationToken ct)
    {
        if (!DemoData.Products.Any(x => x.Id == productId)) throw new KeyNotFoundException("找不到商品。");
        lock (gate) watchlist.Add((userId, productId));
        return Task.CompletedTask;
    }
    public Task RemoveWatchAsync(long userId, long productId, CancellationToken ct)
    {
        lock (gate) watchlist.Remove((userId, productId));
        return Task.CompletedTask;
    }
    public Task<UserPosition?> GetPositionAsync(long userId, long productId, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(positions.GetValueOrDefault((userId, productId)));
    }
    public Task SavePositionAsync(UserPosition position, CancellationToken ct)
    {
        lock (gate)
        {
            if (!watchlist.Contains((position.UserId, position.ProductId))) throw new InvalidOperationException("請先追蹤此商品。");
            positions[(position.UserId, position.ProductId)] = position;
        }
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<AnalysisRecord>> GetHistoryAsync(long userId, long? productId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AnalysisRecord>>(history.Where(x => x.UserId == userId && (productId == null || x.ProductId == productId)).ToArray());
    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(true);
}
