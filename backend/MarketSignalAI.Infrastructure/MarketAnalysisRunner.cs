using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;

namespace MarketSignalAI.Infrastructure;

public sealed class MarketAnalysisRunner(
    IMarketDataProvider marketData, IMarketStore marketStore, ISignalStore signals,
    IEnumerable<IAIAnalyst> analysts, ILogger<MarketAnalysisRunner> logger, IMarketReferenceStore? references = null, IAIProviderSettingsStore? settings = null, IPromptStore? prompts = null, ITwMarketContextProvider? twMarket = null, INewsEvidenceResolver? newsEvidence = null) : IMarketAnalysisRunner
{
    public Task<ProductRunResult> RunProductAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate = null) =>
        RunProductCoreAsync(symbol, ct, expectedTradeDate, new ReferenceDataCache(marketData),
            new TwMarketContextBatchCache(twMarket));
    public Task<ProductRunResult> RunProductAsync(string symbol,IReadOnlyList<string>? providers,CancellationToken ct) =>
        RunProductCoreAsync(symbol,ct,null,new ReferenceDataCache(marketData),new TwMarketContextBatchCache(twMarket),providers);
    public Task<ProductRunResult> RunProductAsync(string symbol, IReadOnlyList<string>? providers, bool refreshNewsBeforeAnalysis, CancellationToken ct) =>
        RunProductCoreAsync(symbol, ct, null, new ReferenceDataCache(marketData), new TwMarketContextBatchCache(twMarket), providers, refreshNewsBeforeAnalysis);
    private async Task<ProductRunResult> RunProductCoreAsync(string symbol, CancellationToken ct, DateOnly? expectedTradeDate, ReferenceDataCache referenceCache, TwMarketContextBatchCache twCache, IReadOnlyList<string>? requested = null, bool refreshNewsBeforeAnalysis = false, NewsEvidenceContext? resolvedNews = null)
    {
        var product = await signals.GetProductAsync(symbol, "TW", ct) ?? throw new KeyNotFoundException("找不到台股商品。");
        var users = await marketStore.GetTrackingUserIdsAsync(product.Id, ct);
        if (users.Count == 0) throw new InvalidOperationException("請先追蹤此商品。");
        var enabled = await signals.GetProvidersAsync(ct);
        HashSet<string>? selection=null;
        if(requested is not null)
        {
            if(requested.Count==0 || requested.Any(x=>string.IsNullOrWhiteSpace(x) || !enabled.Any(p=>string.Equals(p.Code,x,StringComparison.OrdinalIgnoreCase))) || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=requested.Count)
                throw new ArgumentException("providers must contain unique known provider codes.");
            selection=requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var plans=new Dictionary<long,IReadOnlyList<AIProviderSetting>?>();
        foreach(var userId in users)
        {
            var rows=settings is null ? null : await settings.GetAsync(userId,ct);
            if(selection is not null && rows is not null && rows.Any(x=>selection.Contains(x.Provider) && !x.Enabled))
                throw new ArgumentException("A requested provider is disabled. Enable it in AI settings first.");
            plans[userId]=rows;
        }
        var newsContext = resolvedNews ?? (newsEvidence is null ? null : await newsEvidence.ResolveAsync(refreshNewsBeforeAnalysis, ct));
        logger.LogInformation("Product fetching {Symbol}", symbol);
        var snapshot = await marketData.GetSnapshotAsync(product.Symbol, ct);
        if (snapshot.Symbol != product.Symbol) throw new InvalidDataException("Market symbol mismatch.");
        var quoteDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.MarketTime, TaiwanZone).DateTime);
        if (expectedTradeDate is { } expected && quoteDate != expected)
            throw new InvalidDataException($"Yahoo quote is stale for {product.Symbol}: {quoteDate}.");
        var saved = await marketStore.SaveSnapshotAsync(product.Id, snapshot, ct);
        logger.LogInformation("Market snapshot saved {Symbol} {SnapshotId} {MarketTime}", symbol, saved.Id, snapshot.MarketTime);

        IReadOnlyList<HistoricalPrice> history;
        try
        {
            history = await marketData.GetHistoricalPricesAsync(product.Symbol, quoteDate.AddDays(-30), quoteDate, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Historical prices unavailable {Symbol}", symbol); history = []; }

        var targetReturns = TargetReturnsCalculator.Calculate(snapshot, history);
        TwMarketContext? twContext = null;
        if (twMarket is not null)
        {
            try { twContext = await twCache.GetAsync(product.Symbol, quoteDate, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TW market context unavailable {Symbol}", product.Symbol);
                twContext = TwMarketContext.Unavailable("Official TWSE context request failed; do not infer missing data.");
            }
        }
        var adapters = analysts.ToDictionary(x => x.ProviderCode, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<ProviderRunResult>();
        foreach (var userId in users)
        {
            var position = await signals.GetPositionAsync(userId, product.Id, ct);
            var records = await signals.GetHistoryAsync(userId, product.Id, ct);
            var prior = records.Where(x => !x.Model.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.AIProviderId).Select(g => g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First())
                .Where(x => enabled.Any(p => p.Id == x.AIProviderId))
                .Select(x => new PreviousProviderDecision(enabled.Single(p => p.Id == x.AIProviderId).Code, x.Model, x.CreatedAt, x.Result))
                .OrderBy(x => x.Provider, StringComparer.Ordinal).ToArray();
            var referenceContexts = new List<MarketReferenceContext>();
            if (references is not null)
                foreach (var mapping in await references.GetReferencesAsync(userId, product.Id, ct))
                    referenceContexts.Add(await referenceCache.GetAsync(mapping, quoteDate.AddDays(-30), quoteDate, ct));
            // Freeze one complete context before calling any provider. Legacy singular input stays null;
            // every provider receives the same labelled prior decisions, including its own.
            var promptSettings = prompts is null ? null : await prompts.GetAsync(userId, ct);
            var activePrompt = promptSettings is null
                ? new PromptVersion(0, "investment-analysis-v1", AnalysisProtocol.CommonInstructions, DateTime.MinValue)
                : promptSettings.Versions.Single(x => x.Id == promptSettings.ActiveVersionId);
            var promptSnapshot = AnalysisProtocol.CapturePrompt(activePrompt, product.IsLeveraged);
            var context = new MarketContext(product, snapshot, history, position, null)
            { MarketReferences = referenceContexts.ToArray(), PreviousDecisions = prior, Prompt = promptSnapshot,
                TwMarketContext = twContext, TargetReturns = targetReturns, NewsContext = newsContext };
            var sharedInput = JsonSerializer.Deserialize<JsonElement>(AnalysisProtocol.Input(context));
            var providerSettings=plans[userId];
            foreach (var provider in enabled.Where(p=>(selection is null || selection.Contains(p.Code)) &&
                (providerSettings is null || providerSettings.Any(x=>x.Provider==p.Code && x.Enabled))))
            {
                ct.ThrowIfCancellationRequested();
                if (!adapters.TryGetValue(provider.Code, out var analyst))
                {
                    outcomes.Add(new(provider.Code, "UNAVAILABLE", null, "AI adapter is not configured.", null, null));
                    logger.LogWarning("AI analysis failed {Symbol} {Provider}: adapter unavailable", symbol, provider.Code);
                    continue;
                }
                var configured=providerSettings?.Single(x=>x.Provider==provider.Code);
                if(configured is not null)analyst=analyst.WithModel(configured.ConfiguredModel);
                logger.LogInformation("AI analysis started {Symbol} {Provider} {UserId}", symbol, provider.Code, userId);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(analyst.Timeout);
                    var answer = await analyst.AnalyzeAsync(context, timeout.Token);
                    AnalysisResultValidator.Validate(answer);
                    var input = JsonSerializer.Serialize(new { promptVersion = promptSnapshot.Version, promptVersionId = promptSnapshot.Id, promptSnapshot, skillIdentifiers = promptSnapshot.Skills.Select(x => x.Identifier).ToArray(), configuredModel = configured?.ConfiguredModel ?? analyst.Model, isMock = answer.Model.StartsWith("mock", StringComparison.OrdinalIgnoreCase), product, snapshot, history, position, previousDecision = context.PreviousDecision, marketReferences = context.MarketReferences, previousDecisions = context.PreviousDecisions, analysisInput = sharedInput, twMarketContext = twContext, targetReturns, newsContext, instructions = answer.Instructions, reasoningEffort = answer.ReasoningEffort });
                    var savedAnalysis = await marketStore.SaveAnalysisAsync(new(0, userId, product.Id, provider.Id,
                        answer.Model, answer.Analysis, input, answer.RawResponse, DateTime.UtcNow, answer.Usage, newsContext?.NewsContextId), ct);
                    outcomes.Add(new(provider.Code, "COMPLETED", savedAnalysis.Id, null, answer.Model, answer.Analysis, answer.Usage, configured?.ConfiguredModel ?? analyst.Model));
                    logger.LogInformation("AI analysis completed {Symbol} {Provider} {AnalysisId} {Model} {InputTokens} {OutputTokens}",
                        symbol, provider.Code, savedAnalysis.Id, answer.Model, answer.Usage?.InputTokens, answer.Usage?.OutputTokens);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    var error = ex is OperationCanceledException ? "AI timeout." : ex is AIProviderException ? ex.Message : "AI analysis failed.";
                    var usage = (ex as AIProviderException)?.Usage;
                    outcomes.Add(new(provider.Code, "FAILED", null, error, analyst.Model, null, usage, configured?.ConfiguredModel ?? analyst.Model));
                    logger.LogError(ex, "AI analysis failed {Symbol} {Provider} {UserId} {InputTokens} {OutputTokens}",
                        symbol, provider.Code, userId, usage?.InputTokens, usage?.OutputTokens);
                    try
                    {
                        await marketStore.SaveProviderFailureAsync(new(userId, product.Id, provider.Id, analyst.Model,
                            analyst.ReasoningEffort, error, usage, DateTime.UtcNow), ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception persistenceError) { logger.LogError(persistenceError, "Failed to persist provider failure {Provider}", provider.Code); }
                }
            }
        }
        return new(product.Symbol, saved.Id, outcomes);
    }

    public async Task<BatchRunResult> RunAllAsync(CancellationToken ct, DateOnly? expectedTradeDate = null)
    {
        var products = await marketStore.GetActiveTrackedProductsAsync("TW", ct);
        var completed = new List<ProductRunResult>(); var failed = new List<ProductRunFailure>();
        var referenceCache = new ReferenceDataCache(marketData);
        var twCache = new TwMarketContextBatchCache(twMarket);
        var batchNews = newsEvidence is null ? null : await newsEvidence.ResolveAsync(false, ct);
        foreach (var product in products)
        {
            ct.ThrowIfCancellationRequested();
            try { completed.Add(await RunProductCoreAsync(product.Symbol, ct, expectedTradeDate, referenceCache, twCache, resolvedNews: batchNews)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogError(ex, "Product analysis failed {Symbol}", product.Symbol); failed.Add(new(product.Symbol, ex.Message)); }
        }
        return new(completed, failed);
    }

    private static readonly TimeZoneInfo TaiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
}
