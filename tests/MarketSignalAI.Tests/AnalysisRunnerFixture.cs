using MarketSignalAI.Application;
using MarketSignalAI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace MarketSignalAI.Tests;
internal static class AnalysisRunnerFixture
{
    internal static MarketAnalysisRunner Create(IMarketDataProvider marketData, IMarketStore marketStore, ISignalStore signals,
        IEnumerable<IAIAnalyst> analysts, ILogger<MarketAnalysisRunner> logger, IMarketReferenceStore? references = null,
        IAIProviderSettingsStore? settings = null, IPromptStore? prompts = null, ITwMarketContextProvider? twMarket = null,
        INewsEvidenceResolver? newsEvidence = null) => new(marketStore, signals,
            new AnalysisContextBuilder(marketData, marketStore, signals, NullLogger<AnalysisContextBuilder>.Instance, references, prompts, twMarket),
            new AIProviderExecutionService(marketStore, analysts, NullLogger<AIProviderExecutionService>.Instance), logger, settings, newsEvidence);
}
