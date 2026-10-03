using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;
namespace MarketSignalAI.Infrastructure;

// Owns market acquisition and per-user context assembly; never resolves news or executes analysts.
public sealed class AnalysisContextBuilder(IMarketDataProvider marketData, IMarketStore marketStore, ISignalStore signals,
    ILogger<AnalysisContextBuilder> logger, IMarketReferenceStore? references = null, IPromptStore? prompts = null,
    ITwMarketContextProvider? twMarket = null)
{
    internal AnalysisEvidenceScope CreateScope() => new(new ReferenceDataCache(marketData), new TwMarketContextBatchCache(twMarket));

    internal async Task<ProductMarketEvidence> FetchMarketAsync(Product product, DateOnly? expectedTradeDate,
        AnalysisEvidenceScope scope, CancellationToken ct)
    {
        var symbol = product.Symbol;
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
            try { twContext = await scope.TwMarket.GetAsync(product.Symbol, quoteDate, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TW market context unavailable {Symbol}", product.Symbol);
                twContext = TwMarketContext.Unavailable("Official TWSE context request failed; do not infer missing data.");
            }
        }
        return new(saved, quoteDate, history, targetReturns, twContext);
    }

    internal async Task<PreparedAnalysisContext> BuildAsync(Product product, long userId, IReadOnlyList<AIProvider> enabled,
        ProductMarketEvidence evidence, NewsEvidenceContext? newsContext, AnalysisEvidenceScope scope, CancellationToken ct)
    {
        var snapshot = evidence.SavedSnapshot.Snapshot;
        var quoteDate = evidence.QuoteDate;
        var history = evidence.History;
        var twContext = evidence.TwContext;
        var targetReturns = evidence.TargetReturns;
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
                referenceContexts.Add(await scope.References.GetAsync(mapping, quoteDate.AddDays(-30), quoteDate, ct));
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
        return new(context, sharedInput);
    }

    private static readonly TimeZoneInfo TaiwanZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
}

// These transient values never change the public input/response models or their JSON contracts.
internal sealed record AnalysisEvidenceScope(ReferenceDataCache References, TwMarketContextBatchCache TwMarket);
internal sealed record ProductMarketEvidence(StoredMarketSnapshot SavedSnapshot, DateOnly QuoteDate,
    IReadOnlyList<HistoricalPrice> History, TargetReturns TargetReturns, TwMarketContext? TwContext);
internal sealed record PreparedAnalysisContext(MarketContext Context, JsonElement SharedInput);
