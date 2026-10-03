namespace MarketSignalAI.Domain;

public sealed record NewsEventSource(string ResultId, string Publisher, string Url, DateTimeOffset PublishedAt, string SourceType);
public sealed record NewsEvent(string Id, string Category, string Title, string Summary, DateTimeOffset? EventTime,
    DateTimeOffset PublishedAt, string Direction, string Importance, int Relevance, int Confidence,
    string TimeQuality, string? EventTimeEvidence, IReadOnlyList<NewsEventSource> Sources)
{ public string? TimeQualityReason { get; init; } }
public sealed record NewsSourceCoverage(string Publisher, string Topic, string Status, int ResultCount, string? Limitation);
public sealed record NewsContext(long Id, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, DateTimeOffset GeneratedAt,
    string SearchProvider, string CollectorProvider, string ConfiguredModel, string CollectorModel, string Status,
    IReadOnlyList<NewsEvent> Events, IReadOnlyList<NewsSourceCoverage> Coverage,
    int SearchResultCount, int FilteredResultCount, int UnknownTimeCount, int CollectorInputCount,
    TokenUsage? Usage, string PromptVersion)
{ public int EventCount => Events.Count; }
public sealed record NewsRefreshAudit(string RawSearchSnapshotJson, string NormalizedInputJson,
    string PromptSnapshot, string SchemaSnapshot, string RawCollectorResponse);
public sealed record NewsRefreshFailure(DateTimeOffset WindowStart, DateTimeOffset WindowEnd, DateTimeOffset FailedAt,
    string Stage, string Error, string? Model, TokenUsage? Usage, string? RawCollectorResponse = null);
