namespace MarketSignalAI.Application;
public sealed record AnalysisExecutionSettings(bool RefreshNewsBeforeAnalysis = false);
public interface IAnalysisExecutionSettingsStore
{
    Task<AnalysisExecutionSettings> GetAsync(long userId, CancellationToken ct);
    Task SaveAsync(long userId, AnalysisExecutionSettings settings, CancellationToken ct);
}
