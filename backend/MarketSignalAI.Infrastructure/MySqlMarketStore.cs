using System.Text.Json;
using Dapper;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
using MySqlConnector;

namespace MarketSignalAI.Infrastructure;

public sealed class MySqlMarketStore(string connectionString) : IMarketStore
{
    private MySqlConnection Connection() => new(connectionString);
    private static CommandDefinition Cmd(string sql, object? args, CancellationToken ct) => new(sql, args, cancellationToken: ct);
    private const string SnapshotSelect = "SELECT s.Id,s.ProductId,p.Symbol,s.Price,s.`Open`,s.High,s.Low,s.PreviousClose,s.Volume,s.MarketTime,s.FetchedAt FROM MarketSnapshots s JOIN Products p ON p.Id=s.ProductId";

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = Connection();
        foreach (var migration in new[] { "002_market_pipeline.sql", "003_openai_usage.sql" })
        {
            await using var stream = typeof(MySqlMarketStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations." + migration)
                ?? throw new InvalidOperationException("Market migration resource missing.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!string.IsNullOrWhiteSpace(statement)) await db.ExecuteAsync(Cmd(statement, null, ct));
        }
        foreach (var table in new[] { "AIAnalysisUsage", "AIProviderFailures" })
        {
            var exists = await db.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='CachedTokens'", new { table }, ct));
            if (exists == 0) await db.ExecuteAsync(Cmd($"ALTER TABLE {table} ADD COLUMN CachedTokens BIGINT NULL", null, ct));
        }
    }

    public async Task<StoredMarketSnapshot?> GetLatestSnapshotAsync(long productId, CancellationToken ct)
    {
        await using var db = Connection();
        var row = await db.QuerySingleOrDefaultAsync<SnapshotRow>(Cmd($"{SnapshotSelect} WHERE s.ProductId=@productId ORDER BY s.FetchedAt DESC,s.Id DESC LIMIT 1", new { productId }, ct));
        return row is null ? null : ToSnapshot(row);
    }
    public async Task<StoredMarketSnapshot> SaveSnapshotAsync(long productId, MarketSnapshot snapshot, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        var id = await db.ExecuteScalarAsync<long>(Cmd("""
            INSERT INTO MarketSnapshots(ProductId,Price,`Open`,High,Low,PreviousClose,Volume,MarketTime,FetchedAt)
            VALUES(@productId,@Price,@Open,@High,@Low,@PreviousClose,@Volume,@marketTime,@fetchedAt);
            SELECT LAST_INSERT_ID();
            """, new { productId, snapshot.Price, snapshot.Open, snapshot.High, snapshot.Low, snapshot.PreviousClose,
                snapshot.Volume, marketTime = snapshot.MarketTime.UtcDateTime, fetchedAt = snapshot.FetchedAt.UtcDateTime }, ct));
        return new(id, productId, snapshot);
    }
    public async Task<TradingDay?> GetTradingDayAsync(DateOnly date, string market, CancellationToken ct)
    {
        await using var db = Connection();
        var row = await db.QuerySingleOrDefaultAsync<TradingDayRow>(Cmd("SELECT TradeDate,Market,Status,CheckedAt FROM TradingDays WHERE TradeDate=@date AND Market=@market", new { date = date.ToDateTime(TimeOnly.MinValue), market }, ct));
        return row is null ? null : new(DateOnly.FromDateTime(row.TradeDate), row.Market, row.Status, new(row.CheckedAt, TimeSpan.Zero));
    }
    public async Task SaveTradingDayAsync(TradingDay day, CancellationToken ct)
    {
        await using var db = Connection();
        await db.ExecuteAsync(Cmd("""
            INSERT INTO TradingDays(TradeDate,Market,Status,CheckedAt) VALUES(@date,@Market,@Status,@checkedAt)
            ON DUPLICATE KEY UPDATE Status=@Status,CheckedAt=@checkedAt
            """, new { date = day.TradeDate.ToDateTime(TimeOnly.MinValue), day.Market, day.Status, checkedAt = day.CheckedAt.UtcDateTime }, ct));
    }
    public async Task<IReadOnlyList<Product>> GetActiveTrackedProductsAsync(string market, CancellationToken ct)
    {
        await using var db = Connection();
        return (await db.QueryAsync<Product>(Cmd("""
            SELECT DISTINCT p.Id,p.Symbol,p.Name,p.Market,p.AssetType,p.IsLeveraged,p.QuantityUnit,p.UnitSize
            FROM Products p JOIN UserWatchlists w ON w.ProductId=p.Id
            WHERE p.Market=@market AND p.IsActive=1 ORDER BY p.Symbol
            """, new { market }, ct))).AsList();
    }
    public async Task<IReadOnlyList<long>> GetTrackingUserIdsAsync(long productId, CancellationToken ct)
    {
        await using var db = Connection();
        return (await db.QueryAsync<long>(Cmd("SELECT UserId FROM UserWatchlists WHERE ProductId=@productId", new { productId }, ct))).AsList();
    }
    public async Task<AnalysisRecord> SaveAnalysisAsync(AnalysisRecord result, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var id = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO AIAnalysisResults(UserId,ProductId,AIProviderId,Model,Action,Quantity,Confidence,AnalysisJson,InputSnapshotJson,RawResponse,CreatedAt)
            VALUES(@UserId,@ProductId,@AIProviderId,@Model,@Action,@Quantity,@Confidence,@AnalysisJson,@InputSnapshotJson,@RawResponse,@CreatedAt);
            SELECT LAST_INSERT_ID();
            """, new { result.UserId, result.ProductId, result.AIProviderId, result.Model,
                result.Result.Action, result.Result.Quantity, result.Result.Confidence,
                AnalysisJson = JsonSerializer.Serialize(result.Result), result.InputSnapshotJson, result.RawResponse, result.CreatedAt },
                transaction, cancellationToken: ct));
        if (result.Usage is { } usage)
            await db.ExecuteAsync(new CommandDefinition("INSERT INTO AIAnalysisUsage(AnalysisId,InputTokens,OutputTokens,CachedTokens) VALUES(@id,@InputTokens,@OutputTokens,@CachedTokens)",
                new { id, usage.InputTokens, usage.OutputTokens, usage.CachedTokens }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return result with { Id = id };
    }
    public async Task SaveProviderFailureAsync(ProviderFailure failure, CancellationToken ct)
    {
        await using var db = Connection();
        await db.ExecuteAsync(Cmd("""
            INSERT INTO AIProviderFailures(UserId,ProductId,AIProviderId,Model,ReasoningEffort,Error,InputTokens,OutputTokens,CachedTokens,CreatedAt)
            VALUES(@UserId,@ProductId,@AIProviderId,@Model,@ReasoningEffort,@Error,@InputTokens,@OutputTokens,@CachedTokens,@CreatedAt)
            """, new { failure.UserId, failure.ProductId, failure.AIProviderId, failure.Model, failure.ReasoningEffort,
                failure.Error, InputTokens = failure.Usage?.InputTokens, OutputTokens = failure.Usage?.OutputTokens, CachedTokens = failure.Usage?.CachedTokens, failure.CreatedAt }, ct));
    }
    private static StoredMarketSnapshot ToSnapshot(SnapshotRow x) => new(x.Id, x.ProductId,
        new(x.Symbol, x.Price, x.Open, x.High, x.Low, x.PreviousClose, x.Volume,
            new DateTimeOffset(DateTime.SpecifyKind(x.MarketTime, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(x.FetchedAt, DateTimeKind.Utc))));
    private sealed class SnapshotRow
    {
        public long Id { get; set; } public long ProductId { get; set; } public string Symbol { get; set; } = "";
        public decimal Price { get; set; } public decimal Open { get; set; } public decimal High { get; set; }
        public decimal Low { get; set; } public decimal PreviousClose { get; set; } public long Volume { get; set; }
        public DateTime MarketTime { get; set; } public DateTime FetchedAt { get; set; }
    }
    private sealed class TradingDayRow
    {
        public DateTime TradeDate { get; set; } public string Market { get; set; } = "";
        public string Status { get; set; } = ""; public DateTime CheckedAt { get; set; }
    }
}
