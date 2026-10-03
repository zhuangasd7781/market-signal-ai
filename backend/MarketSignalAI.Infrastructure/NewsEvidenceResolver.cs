using System.Text.Json;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;
namespace MarketSignalAI.Infrastructure;

public sealed class NewsIntelligenceOptions { public int MaxAgeMinutes { get; set; } = 180; }
public sealed class NewsEvidenceResolver(INewsContextStore store, INewsContextRefresher refresher,
    NewsIntelligenceOptions options, TimeProvider clock, ILogger<NewsEvidenceResolver> logger) : INewsEvidenceResolver
{
    public async Task<NewsEvidenceContext> ResolveAsync(bool refreshBeforeAnalysis, CancellationToken ct)
    {
        NewsContext? context = null;
        var refreshFailed = false;
        string? limitation = null;
        if (refreshBeforeAnalysis)
        {
            try { context = await refresher.RefreshAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { refreshFailed = true; limitation = "News refresh failed; using previous valid context if available.";
                logger.LogWarning("News refresh before analysis failed; resolving previous valid evidence"); }
        }
        if (context is null)
        {
            try { context = await store.GetLatestValidAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { limitation = "News evidence storage unavailable; do not infer news.";
                logger.LogWarning("News evidence storage unavailable during analysis"); }
        }
        var now = clock.GetUtcNow();
        if (context is null || context.Status is not ("AVAILABLE" or "PARTIAL"))
            return new(null, null, null, null, "UNAVAILABLE", "UNAVAILABLE", now, options.MaxAgeMinutes,
                refreshFailed, limitation ?? "No valid news evidence available; do not invent current news.", []);
        // Freeze independently from store-owned collections, once before any investment provider runs.
        var events = JsonSerializer.Deserialize<NewsEvent[]>(JsonSerializer.Serialize(context.Events))!;
        var stale = now - context.GeneratedAt > TimeSpan.FromMinutes(options.MaxAgeMinutes);
        var freshness = context.GeneratedAt > now ? "UNKNOWN" : stale ? "STALE" : "FRESH";
        logger.LogInformation("Analysis news evidence resolved {NewsContextId} {Freshness} {EventCount} {NewsRefreshFailed}",
            context.Id, freshness, events.Length, refreshFailed);
        return new(context.Id, context.GeneratedAt, context.WindowStart, context.WindowEnd, context.Status,
            freshness, now, options.MaxAgeMinutes, refreshFailed, limitation, events);
    }
}
