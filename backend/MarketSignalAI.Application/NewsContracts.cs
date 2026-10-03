using MarketSignalAI.Domain;
namespace MarketSignalAI.Application;

public sealed record NewsSearchQuery(DateTimeOffset WindowStart, DateTimeOffset WindowEnd, IReadOnlyList<string> Topics);
public sealed record NewsSearchResult(string Id, string Topic, string Title, string Url, string Publisher,
    DateTimeOffset? PublishedAt, string Snippet, string SourceType, string TimeQuality);
public sealed record NewsSearchBatch(string Provider, IReadOnlyList<NewsSearchResult> Results, IReadOnlyList<NewsSourceCoverage> Coverage);
public interface INewsSearchProvider { Task<NewsSearchBatch> SearchAsync(NewsSearchQuery query, CancellationToken ct); }
public sealed record NewsIntelligenceInput(DateTimeOffset WindowStart, DateTimeOffset WindowEnd, IReadOnlyList<NewsSearchResult> Results);
public sealed record NewsEventDraft(string Category, string Title, string Summary, DateTimeOffset? EventTime,
    string Direction, string Importance, int Relevance, int Confidence, string TimeQuality,
    string? EventTimeEvidence, string[] SourceIds);
public sealed record NewsCollection(IReadOnlyList<NewsEventDraft> Events, string ActualModel, TokenUsage Usage,
    string PromptVersion, string PromptSnapshot, string SchemaSnapshot, string RawResponse);
public interface INewsIntelligenceCollector
{
    string ConfiguredModel { get; }
    Task<NewsCollection> CollectAsync(NewsIntelligenceInput input, CancellationToken ct);
}
public interface INewsContextStore
{
    Task<NewsContext> AppendAsync(NewsContext context, NewsRefreshAudit audit, CancellationToken ct);
    Task<NewsContext?> GetLatestAsync(CancellationToken ct);
    async Task<NewsContext?> GetLatestValidAsync(CancellationToken ct)
    {
        var latest = await GetLatestAsync(ct);
        return latest?.Status is "AVAILABLE" or "PARTIAL" ? latest : null;
    }
    Task<NewsContext?> GetAsync(long id, CancellationToken ct);
    Task AppendFailureAsync(NewsRefreshFailure failure, CancellationToken ct);
}
public sealed class NewsRefreshGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }
public sealed class NewsPipelineException(string stage, string message, string? model = null, TokenUsage? usage = null, string? rawResponse = null) : Exception(message)
{
    public string Stage { get; } = stage;
    public string? Model { get; } = model;
    public TokenUsage? Usage { get; } = usage;
    public string? RawResponse { get; } = rawResponse;
}

public interface INewsContextRefresher { Task<NewsContext> RefreshAsync(CancellationToken ct); }
