using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using Microsoft.Extensions.Logging;
namespace MarketSignalAI.Infrastructure;

public sealed class NewsContextRefresher(INewsSearchProvider search, INewsIntelligenceCollector collector, INewsContextStore store,
    NewsRefreshGate gate, TimeProvider clock, NewsOptions options, ILogger<NewsContextRefresher> logger) : INewsContextRefresher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<NewsContext> RefreshAsync(CancellationToken ct)
    {
        if(!await gate.Semaphore.WaitAsync(0,ct)) throw new InvalidOperationException("市場情報正在更新，請稍後再試。");
        var end=clock.GetUtcNow().ToUniversalTime();var start=end.AddHours(-72);var timer=Stopwatch.StartNew();var stage="SEARCH";
        logger.LogInformation("News refresh started {WindowStart} {WindowEnd}",start,end);
        try
        {
            var batch=await search.SearchAsync(new(start,end,["MACRO","TECH","TAIWAN"]),ct);
            var unknown=batch.Results.Count(x=>x.PublishedAt is null);
            var filtered=batch.Results.Where(x=>x.PublishedAt>=start && x.PublishedAt<=end && x.TimeQuality=="VERIFIED")
                .GroupBy(x=>x.Url,StringComparer.Ordinal).Select(g=>g.OrderByDescending(x=>x.PublishedAt).First())
                .OrderByDescending(x=>x.PublishedAt).ToArray();
            // Bounded, balanced topic selection; a busy Taiwan feed cannot crowd out macro/tech.
            var perTopic=Math.Max(1,options.MaxInputArticles/3);
            var selected=filtered.GroupBy(x=>x.Topic).SelectMany(g=>g.Take(perTopic)).OrderByDescending(x=>x.PublishedAt).Take(options.MaxInputArticles).ToArray();
            logger.LogInformation("News search filtered {SearchResultCount} {FilteredResultCount} {UnknownTimeCount} {CollectorInputCount}",batch.Results.Count,filtered.Length,unknown,selected.Length);
            var input=new NewsIntelligenceInput(start,end,selected);stage="FLASH";
            var collected=await collector.CollectAsync(input,ct);
            var events=Validate(collected.Events,input);
            if(string.IsNullOrWhiteSpace(collected.ActualModel) || collected.Usage.InputTokens<0 || collected.Usage.OutputTokens<0 || collected.Usage.CachedTokens<0 || collected.Usage.CachedTokens>collected.Usage.InputTokens)
                throw new NewsPipelineException("VALIDATION","新聞模型回傳的 Model 或 Token Usage 無效。",collected.ActualModel,collected.Usage);
            var context=new NewsContext(0,start,end,clock.GetUtcNow().ToUniversalTime(),batch.Provider,"deepseek",collector.ConfiguredModel,
                collected.ActualModel,batch.Provider=="FIXED_RSS" || unknown>0 || selected.Length<filtered.Length || batch.Coverage.Any(x=>x.Status!="AVAILABLE") || events.Any(x=>x.TimeQualityReason is not null) ? "PARTIAL" : events.Count==0 ? "EMPTY" : "AVAILABLE",
                events,batch.Coverage,batch.Results.Count,filtered.Length,unknown,selected.Length,collected.Usage,collected.PromptVersion);
            stage="PERSISTENCE";
            var saved=await store.AppendAsync(context,new(JsonSerializer.Serialize(batch,Json),JsonSerializer.Serialize(input,Json),collected.PromptSnapshot,collected.SchemaSnapshot,collected.RawResponse),ct);
            logger.LogInformation("News Flash processing success {NewsContextId} {EventCount} {ActualModel} {InputTokens} {OutputTokens} {ElapsedMs}",saved.Id,events.Count,saved.CollectorModel,saved.Usage?.InputTokens,saved.Usage?.OutputTokens,timer.ElapsedMilliseconds);
            return saved;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            var safe=ex as NewsPipelineException ?? new(stage,stage=="FLASH" ? "新聞模型請求失敗，未建立市場情報。" : "市場情報更新失敗，未建立新的情報。",collector.ConfiguredModel);
            logger.LogWarning("News refresh failure {Stage} {Error} {ElapsedMs}",safe.Stage,safe.Message,timer.ElapsedMilliseconds);
            try { using var failureTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));await store.AppendFailureAsync(new(start,end,clock.GetUtcNow(),safe.Stage,safe.Message,safe.Model,safe.Usage,safe.RawResponse),failureTimeout.Token); }
            catch(Exception) { logger.LogError("News refresh failure persistence unavailable"); }
            throw safe;
        }
        finally { gate.Semaphore.Release(); }
    }

    private static bool SupportedRecapDate(string evidence,DateTimeOffset start)
    {
        var match=Regex.Match(evidence,@"\d{4}-\d{2}-\d{2}");
        return match.Success && DateOnly.TryParseExact(match.Value,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date) && date<DateOnly.FromDateTime(start.UtcDateTime);
    }
    private static bool SupportedTime(string evidence,DateTimeOffset time)
    {
        var match=Regex.Match(evidence,@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})",RegexOptions.IgnoreCase);
        return match.Success && DateTimeOffset.TryParse(match.Value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var supported) && supported==time;
    }
    public static IReadOnlyList<NewsEvent> Validate(IReadOnlyList<NewsEventDraft> drafts,NewsIntelligenceInput input)
    {
        if(drafts.Count>20) throw new NewsPipelineException("VALIDATION","新聞事件超過數量限制。");
        var sources=input.Results.ToDictionary(x=>x.Id,StringComparer.Ordinal);var used=new HashSet<string>();var titles=new HashSet<string>();var events=new List<NewsEvent>();
        foreach(var draft in drafts)
        {
            var e=draft;
            if(e.Category is not ("MACRO" or "TECH" or "TAIWAN") || e.Direction is not ("POSITIVE" or "NEGATIVE" or "NEUTRAL" or "MIXED" or "UNKNOWN") || e.Importance is not ("HIGH" or "MEDIUM" or "LOW") ||
                e.TimeQuality is not ("UNKNOWN" or "SUPPORTED" or "RECAP") || e.EventTimeEvidence?.Length>500 || e.Relevance is <0 or >100 || e.Confidence is <0 or >100 || string.IsNullOrWhiteSpace(e.Title) || e.Title.Length>300 || string.IsNullOrWhiteSpace(e.Summary) || e.Summary.Length>1800 ||
                Regex.IsMatch(e.Title+" "+e.Summary,@"\b(ADD|HOLD|REDUCE|EXIT)\b",RegexOptions.IgnoreCase) || e.SourceIds.Length==0 || e.SourceIds.Distinct().Count()!=e.SourceIds.Length ||
                e.SourceIds.Any(x=>!sources.ContainsKey(x) || !used.Add(x)) || !titles.Add(e.Title.Trim().ToUpperInvariant()))
                throw new NewsPipelineException("VALIDATION","新聞模型回傳無效、重複、無來源或包含交易指令的事件。");
            var articles=e.SourceIds.Select(x=>sources[x]).ToArray();
            if(articles.Any(x=>x.PublishedAt is null || x.PublishedAt<input.WindowStart || x.PublishedAt>input.WindowEnd)) throw new NewsPipelineException("VALIDATION","新聞來源不在有效時間窗口內。");
            string? timeQualityReason=null;
            if(e.EventTime is null ? !((e.TimeQuality=="UNKNOWN" && e.EventTimeEvidence is null) || (e.TimeQuality=="RECAP" && e.EventTimeEvidence is not null && articles.Any(x=>(x.Title+" "+x.Snippet).Contains(e.EventTimeEvidence,StringComparison.OrdinalIgnoreCase)) && SupportedRecapDate(e.EventTimeEvidence,input.WindowStart))) :
                e.EventTime>input.WindowEnd || e.TimeQuality!=(e.EventTime<input.WindowStart ? "RECAP" : "SUPPORTED") || string.IsNullOrWhiteSpace(e.EventTimeEvidence) ||
                !articles.Any(x=>(x.Title+" "+x.Snippet).Contains(e.EventTimeEvidence!,StringComparison.OrdinalIgnoreCase)) ||
                !SupportedTime(e.EventTimeEvidence!,e.EventTime.Value))
            {
                // Never treat publication time or model-inferred timestamps as event facts.
                // Discard unsupported timing, preserve the original model output in audit, and expose the limitation.
                e=e with {EventTime=null,TimeQuality="UNKNOWN",EventTimeEvidence=null};
                timeQualityReason="Model-proposed event timing was not supported by supplied source text; event time is UNKNOWN.";
            }
            events.Add(new(Guid.NewGuid().ToString("N"),e.Category,e.Title,e.Summary,e.EventTime?.ToUniversalTime(),articles.Max(x=>x.PublishedAt!.Value).ToUniversalTime(),
                e.Direction,e.Importance,e.Relevance,e.Confidence,e.TimeQuality,e.EventTimeEvidence,
                articles.Select(x=>new NewsEventSource(x.Id,x.Publisher,x.Url,x.PublishedAt!.Value.ToUniversalTime(),x.SourceType)).ToArray()) {TimeQualityReason=timeQualityReason});
        }
        return events;
    }
}
