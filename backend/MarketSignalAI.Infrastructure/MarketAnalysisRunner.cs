using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;

namespace MarketSignalAI.Infrastructure;

public sealed class MarketAnalysisRunner(
    IMarketStore marketStore, ISignalStore signals, AnalysisContextBuilder contextBuilder,
    AIProviderExecutionService execution, ILogger<MarketAnalysisRunner> logger,
    IAIProviderSettingsStore? settings = null, INewsEvidenceResolver? newsEvidence = null) : IMarketAnalysisRunner
{
    public Task<ProductRunResult> RunProductAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate = null) =>
        RunProductCoreAsync(symbol, ct, expectedTradeDate, contextBuilder.CreateScope());
    public Task<ProductRunResult> RunProductAsync(string symbol, IReadOnlyList<string>? providers, CancellationToken ct) =>
        RunProductCoreAsync(symbol, ct, null, contextBuilder.CreateScope(), providers);
    public Task<ProductRunResult> RunProductAsync(string symbol, IReadOnlyList<string>? providers, bool refreshNewsBeforeAnalysis, CancellationToken ct) =>
        RunProductCoreAsync(symbol, ct, null, contextBuilder.CreateScope(), providers, refreshNewsBeforeAnalysis);
    private async Task<ProductRunResult> RunProductCoreAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate, AnalysisEvidenceScope scope, IReadOnlyList<string>? requested = null, bool refreshNewsBeforeAnalysis = false, NewsEvidenceContext? resolvedNews = null)
    {
        var product = await signals.GetProductAsync(symbol, "TW", ct) ?? throw new KeyNotFoundException("找不到台股商品。");
        var users = await marketStore.GetTrackingUserIdsAsync(product.Id, ct);
        if (users.Count == 0) throw new InvalidOperationException("請先追蹤此商品。");
        var enabled = await signals.GetProvidersAsync(ct);
        HashSet<string>? selection = null;
        if (requested is not null)
        {
            if(requested.Count == 0 || requested.Any(x => string.IsNullOrWhiteSpace(x) || !enabled.Any(p => string.Equals(p.Code, x, StringComparison.OrdinalIgnoreCase))) || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count)
                throw new ArgumentException("providers must contain unique known provider codes.");
            selection = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var plans = new Dictionary<long, IReadOnlyList<AIProviderSetting>?>();
        foreach (var userId in users)
        {
            var rows = settings is null ? null : await settings.GetAsync(userId, ct);
            if (selection is not null && rows is not null && rows.Any(x => selection.Contains(x.Provider) && !x.Enabled))
                throw new ArgumentException("A requested provider is disabled. Enable it in AI settings first.");
            plans[userId] = rows;
        }
        var newsContext = resolvedNews ?? (newsEvidence is null ? null : await newsEvidence.ResolveAsync(refreshNewsBeforeAnalysis, ct));
        var evidence = await contextBuilder.FetchMarketAsync(product, expectedTradeDate, scope, ct);
        var adapters = execution.GetAdapters();
        var outcomes = new List<ProviderRunResult>();
        foreach (var userId in users)
        {
            var prepared = await contextBuilder.BuildAsync(product, userId, enabled, evidence, newsContext, scope, ct);
            var providerSettings = plans[userId];
            var selected = enabled.Where(p => (selection is null || selection.Contains(p.Code)) &&
                (providerSettings is null || providerSettings.Any(x => x.Provider == p.Code && x.Enabled)));
            outcomes.AddRange(await execution.ExecuteAsync(userId, prepared, selected, providerSettings, adapters, ct));
        }
        return new(product.Symbol, evidence.SavedSnapshot.Id, outcomes);
    }

    public async Task<BatchRunResult> RunAllAsync(CancellationToken ct, DateOnly? expectedTradeDate = null)
    {
        var products = await marketStore.GetActiveTrackedProductsAsync("TW", ct);
        var completed = new List<ProductRunResult>(); var failed = new List<ProductRunFailure>();
        var scope = contextBuilder.CreateScope();
        var batchNews = newsEvidence is null ? null : await newsEvidence.ResolveAsync(false, ct);
        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            try { completed.Add(await RunProductCoreAsync(product.Symbol, ct, expectedTradeDate, scope, resolvedNews: batchNews)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Product analysis failed {Symbol}", product.Symbol); failed.Add(new(product.Symbol, ex.Message)); }
        }
        return new(completed, failed);
    }
}
