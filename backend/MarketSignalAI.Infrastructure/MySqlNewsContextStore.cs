using System.Text.Json;
using Dapper;
using MySqlConnector;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;

public sealed class MySqlNewsContextStore(string connectionString) : INewsContextStore
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        using var stream=typeof(MySqlNewsContextStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.009_news_intelligence.sql") ?? throw new InvalidOperationException("News migration missing.");
        using var reader=new StreamReader(stream);
        foreach(var sql in (await reader.ReadToEndAsync(ct)).Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))await db.ExecuteAsync(new CommandDefinition(sql,cancellationToken:ct));
        if(await db.QuerySingleAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='NewsRefreshFailures' AND COLUMN_NAME='RawCollectorResponse'",cancellationToken:ct))==0)
            await db.ExecuteAsync(new CommandDefinition("ALTER TABLE NewsRefreshFailures ADD COLUMN RawCollectorResponse LONGTEXT NULL",cancellationToken:ct));
    }
    public async Task<NewsContext> AppendAsync(NewsContext c,NewsRefreshAudit audit,CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO NewsContexts(WindowStart,WindowEnd,GeneratedAt,SearchProvider,CollectorProvider,ConfiguredModel,ActualModel,Status,
            SearchResultCount,FilteredResultCount,EventCount,InputTokens,OutputTokens,CachedTokens,PromptVersion,ContextJson,
            RawSearchSnapshotJson,NormalizedInputJson,PromptSnapshot,SchemaSnapshot,RawCollectorResponse)
            VALUES(@start,@end,@generated,@SearchProvider,@CollectorProvider,@ConfiguredModel,@model,@Status,@SearchResultCount,@FilteredResultCount,@count,
            @input,@output,@cached,@PromptVersion,@json,@RawSearchSnapshotJson,@NormalizedInputJson,@PromptSnapshot,@SchemaSnapshot,@RawCollectorResponse)
            """,new {start=c.WindowStart.UtcDateTime,end=c.WindowEnd.UtcDateTime,generated=c.GeneratedAt.UtcDateTime,c.SearchProvider,c.CollectorProvider,c.ConfiguredModel,model=c.CollectorModel,c.Status,
                c.SearchResultCount,c.FilteredResultCount,count=c.Events.Count,input=c.Usage?.InputTokens,output=c.Usage?.OutputTokens,cached=c.Usage?.CachedTokens,c.PromptVersion,json=JsonSerializer.Serialize(c,Json),
                audit.RawSearchSnapshotJson,audit.NormalizedInputJson,audit.PromptSnapshot,audit.SchemaSnapshot,audit.RawCollectorResponse},tx,cancellationToken:ct));
        var id=await db.QuerySingleAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()",transaction:tx,cancellationToken:ct));
        foreach(var e in c.Events)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO NewsEvents(Id,NewsContextId,Category,Title,Summary,EventTime,PublishedAt,Direction,Importance,Relevance,Confidence,TimeQuality,EventTimeEvidence)
                VALUES(@Id,@contextId,@Category,@Title,@Summary,@time,@published,@Direction,@Importance,@Relevance,@Confidence,@TimeQuality,@EventTimeEvidence)
                """,new {e.Id,contextId=id,e.Category,e.Title,e.Summary,time=e.EventTime?.UtcDateTime,published=e.PublishedAt.UtcDateTime,e.Direction,e.Importance,e.Relevance,e.Confidence,e.TimeQuality,e.EventTimeEvidence},tx,cancellationToken:ct));
            foreach(var s in e.Sources)await db.ExecuteAsync(new CommandDefinition("INSERT INTO NewsEventSources(NewsEventId,ResultId,Publisher,Url,PublishedAt,SourceType) VALUES(@eventId,@ResultId,@Publisher,@Url,@published,@SourceType)",new {eventId=e.Id,s.ResultId,s.Publisher,s.Url,published=s.PublishedAt.UtcDateTime,s.SourceType},tx,cancellationToken:ct));
        }
        await tx.CommitAsync(ct);return c with {Id=id};
    }
    public Task<NewsContext?> GetLatestAsync(CancellationToken ct)=>ReadAsync(null,ct);
    public Task<NewsContext?> GetLatestValidAsync(CancellationToken ct)=>ReadAsync(null,ct,true);
    public Task<NewsContext?> GetAsync(long id,CancellationToken ct)=>ReadAsync(id,ct);
    private async Task<NewsContext?> ReadAsync(long? id,CancellationToken ct,bool validOnly=false)
    {
        await using var db=new MySqlConnection(connectionString);
        var row=await db.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(id.HasValue ? "SELECT Id,ContextJson FROM NewsContexts WHERE Id=@id" : validOnly ? "SELECT Id,ContextJson FROM NewsContexts WHERE Status IN ('AVAILABLE','PARTIAL') ORDER BY Id DESC LIMIT 1" : "SELECT Id,ContextJson FROM NewsContexts ORDER BY Id DESC LIMIT 1",new {id},cancellationToken:ct));
        return row is null ? null : (JsonSerializer.Deserialize<NewsContext>(row.ContextJson,Json) ?? throw new InvalidDataException("Invalid stored news context")) with {Id=row.Id};
    }
    private sealed record Row(long Id,string ContextJson);
    public async Task AppendFailureAsync(NewsRefreshFailure f,CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO NewsRefreshFailures(WindowStart,WindowEnd,FailedAt,Stage,Error,Model,InputTokens,OutputTokens,CachedTokens,RawCollectorResponse) VALUES(@start,@end,@failed,@Stage,@Error,@Model,@input,@output,@cached,@RawCollectorResponse)",new {start=f.WindowStart.UtcDateTime,end=f.WindowEnd.UtcDateTime,failed=f.FailedAt.UtcDateTime,f.Stage,f.Error,f.Model,f.RawCollectorResponse,input=f.Usage?.InputTokens,output=f.Usage?.OutputTokens,cached=f.Usage?.CachedTokens},cancellationToken:ct));
    }
}
