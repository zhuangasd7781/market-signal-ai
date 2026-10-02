using MarketSignalAI.Application;

namespace MarketSignalAI.Infrastructure;

// One batch or force request freezes each target's official context once for all providers/users.
internal sealed class TwMarketContextBatchCache(ITwMarketContextProvider? provider)
{
    private readonly Dictionary<(string Symbol, DateOnly Date), TwMarketContext> rows = [];
    internal async Task<TwMarketContext> GetAsync(string symbol, DateOnly date, CancellationToken ct)
    {
        if (provider is null) return TwMarketContext.Unavailable("TWSE provider is not configured.");
        if (rows.TryGetValue((symbol, date), out var found)) return found;
        var result = await provider.GetAsync(symbol, date, ct);
        rows[(symbol, date)] = result;
        return result;
    }
}
