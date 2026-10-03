using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;
namespace MarketSignalAI.Infrastructure;

// Executes a prepared plan only. No market/news/reference/position/prompt stores are dependencies.
public sealed class AIProviderExecutionService(IMarketStore marketStore, IEnumerable<IAIAnalyst> analysts,
    ILogger<AIProviderExecutionService> logger)
{
    internal IReadOnlyDictionary<string, IAIAnalyst> GetAdapters() => analysts.ToDictionary(x => x.ProviderCode, StringComparer.OrdinalIgnoreCase);

    internal async Task<IReadOnlyList<ProviderRunResult>> ExecuteAsync(long userId, PreparedAnalysisContext prepared,
        IEnumerable<AIProvider> providers, IReadOnlyList<AIProviderSetting>? providerSettings,
        IReadOnlyDictionary<string, IAIAnalyst> adapters, CancellationToken ct)
    {
        var context = prepared.Context;
        var product = context.Product;
        var symbol = product.Symbol;
        var newsContext = context.NewsContext;
        var outcomes = new List<ProviderRunResult>();
        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            if (!adapters.TryGetValue(provider.Code, out var analyst))
            {
                outcomes.Add(new(provider.Code, "UNAVAILABLE", null, "AI adapter is not configured.", null, null));
                logger.LogWarning("AI analysis failed {Symbol} {Provider}: adapter unavailable", symbol, provider.Code);
                continue;
            }
            var configured = providerSettings?.Single(x => x.Provider == provider.Code);
            if (configured is not null) analyst = analyst.WithModel(configured.ConfiguredModel);
            logger.LogInformation("AI analysis started {Symbol} {Provider} {UserId}", symbol, provider.Code, userId);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(analyst.Timeout);
                var answer = await analyst.AnalyzeAsync(context, timeout.Token);
                AnalysisResultValidator.Validate(answer);
                var input = AnalysisInputSnapshot.Serialize(prepared, configured?.ConfiguredModel ?? analyst.Model, answer);
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
        return outcomes;
    }
}
