using MarketSignalAI.Domain;
namespace MarketSignalAI.Application;

// Only structured evidence crosses the investment boundary; collection audit stays in the news store.
public sealed record NewsEvidenceContext(long? NewsContextId, DateTimeOffset? GeneratedAt,
    DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd, string Status, string Freshness,
    DateTimeOffset ResolvedAt, int MaxAgeMinutes, bool NewsRefreshFailed, string? Limitation,
    IReadOnlyList<NewsEvent> Events)
{ public int EventCount => Events.Count; }
public interface INewsEvidenceResolver
{
    Task<NewsEvidenceContext> ResolveAsync(bool refreshBeforeAnalysis, CancellationToken ct);
}
