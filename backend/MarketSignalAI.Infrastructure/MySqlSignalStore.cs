using System.Text.Json;
using Dapper;
using MySqlConnector;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Infrastructure;

public sealed class MySqlSignalStore(string connectionString) : ISignalStore
{
    private MySqlConnection Connection() => new(connectionString);
    private static CommandDefinition Command(string sql, object? args, CancellationToken ct) => new(sql, args, cancellationToken: ct);
    public async Task<User?> GetUserAsync(long userId, CancellationToken ct)
    {
        await using var db = Connection();
        return await db.QuerySingleOrDefaultAsync<User>(Command("SELECT Id,DisplayName,Email FROM Users WHERE Id=@userId", new { userId }, ct));
    }
    public async Task<IReadOnlyList<AIProvider>> GetProvidersAsync(CancellationToken ct)
    {
        await using var db = Connection();
        return (await db.QueryAsync<AIProvider>(Command("SELECT Id,Code,DisplayName,SortOrder FROM AIProviders WHERE IsEnabled=1 ORDER BY SortOrder,Id", null, ct))).AsList();
    }
    private const string ProductColumns = "p.Id,p.Symbol,p.Name,p.Market,p.AssetType,p.IsLeveraged,p.QuantityUnit,p.UnitSize";
    public async Task<IReadOnlyList<Product>> SearchProductsAsync(string query, CancellationToken ct)
    {
        await using var db = Connection();
        return (await db.QueryAsync<Product>(Command($"SELECT {ProductColumns} FROM Products p WHERE IsActive=1 AND (LOCATE(@query,Symbol)>0 OR LOCATE(@query,Name)>0) ORDER BY Symbol LIMIT 30", new { query }, ct))).AsList();
    }
    public async Task<Product?> GetProductAsync(string symbol, string market, CancellationToken ct)
    {
        await using var db = Connection();
        return await db.QuerySingleOrDefaultAsync<Product>(Command($"SELECT {ProductColumns} FROM Products p WHERE Symbol=@symbol AND Market=@market AND IsActive=1", new { symbol, market }, ct));
    }
    public async Task<IReadOnlyList<Product>> GetWatchlistAsync(long userId, CancellationToken ct)
    {
        await using var db = Connection();
        return (await db.QueryAsync<Product>(Command($"SELECT {ProductColumns} FROM Products p JOIN UserWatchlists w ON w.ProductId=p.Id WHERE w.UserId=@userId AND p.IsActive=1 ORDER BY w.Id", new { userId }, ct))).AsList();
    }
    public async Task AddWatchAsync(long userId, long productId, CancellationToken ct)
    {
        await using var db = Connection();
        if (await db.ExecuteScalarAsync<int>(Command("SELECT COUNT(*) FROM Products WHERE Id=@productId AND IsActive=1", new { productId }, ct)) == 0)
            throw new KeyNotFoundException("找不到商品。");
        await db.ExecuteAsync(Command("INSERT INTO UserWatchlists(UserId,ProductId) VALUES(@userId,@productId) ON DUPLICATE KEY UPDATE ProductId=@productId", new { userId, productId }, ct));
    }
    public async Task RemoveWatchAsync(long userId, long productId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.ExecuteAsync(Command("DELETE FROM UserWatchlists WHERE UserId=@userId AND ProductId=@productId", new { userId, productId }, ct));
    }
    public async Task<UserPosition?> GetPositionAsync(long userId, long productId, CancellationToken ct)
    {
        await using var db = Connection();
        return await db.QuerySingleOrDefaultAsync<UserPosition>(Command("SELECT UserId,ProductId,Quantity,AverageCost,UpdatedAt FROM UserPositions WHERE UserId=@userId AND ProductId=@productId", new { userId, productId }, ct));
    }
    public async Task SavePositionAsync(UserPosition position, CancellationToken ct)
    {
        await using var db = Connection();
        var affected = await db.ExecuteAsync(Command("""
            INSERT INTO UserPositions(UserId,ProductId,Quantity,AverageCost,UpdatedAt)
            SELECT @UserId,@ProductId,@Quantity,@AverageCost,@UpdatedAt
            FROM UserWatchlists WHERE UserId=@UserId AND ProductId=@ProductId
            ON DUPLICATE KEY UPDATE Quantity=@Quantity,AverageCost=@AverageCost,UpdatedAt=@UpdatedAt
            """, position, ct));
        if (affected == 0) throw new InvalidOperationException("請先追蹤此商品。");
    }
    public async Task<IReadOnlyList<AnalysisRecord>> GetHistoryAsync(long userId, long? productId, CancellationToken ct)
    {
        await using var db = Connection();
        // Home needs only the last two records for each product/provider. Detail history is capped.
        var sql = productId is null ? """
            SELECT * FROM (
              SELECT a.*, u.InputTokens, u.OutputTokens, u.CachedTokens, ROW_NUMBER() OVER(PARTITION BY ProductId,AIProviderId ORDER BY CreatedAt DESC,Id DESC) AS rn
              FROM AIAnalysisResults a LEFT JOIN AIAnalysisUsage u ON u.AnalysisId=a.Id WHERE a.UserId=@userId
            ) ranked WHERE rn<=2
            """ : "SELECT a.*,u.InputTokens,u.OutputTokens,u.CachedTokens FROM AIAnalysisResults a LEFT JOIN AIAnalysisUsage u ON u.AnalysisId=a.Id WHERE a.UserId=@userId AND a.ProductId=@productId ORDER BY a.CreatedAt DESC,a.Id DESC LIMIT 100";
        var rows = await db.QueryAsync<StoredAnalysis>(Command(sql, new { userId, productId }, ct));
        return rows.Select(x => new AnalysisRecord(x.Id, x.UserId, x.ProductId, x.AIProviderId, x.Model,
            JsonSerializer.Deserialize<Analysis>(x.AnalysisJson) ?? throw new InvalidOperationException("Invalid stored analysis."),
            x.InputSnapshotJson, x.RawResponse, DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
            x.InputTokens is { } input && x.OutputTokens is { } output ? new TokenUsage(input, output, x.CachedTokens) : null)).ToArray();
    }
    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        await using var db = Connection();
        return await db.ExecuteScalarAsync<int>(Command("SELECT COUNT(*) FROM AIProviders", null, ct)) > 0;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        // A durable marker ensures deletions and edited providers survive restarts.
        var inserted = await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO SeedVersions(Version) VALUES('phase1-demo-v1')", transaction: transaction, cancellationToken: ct));
        if (inserted == 0) { await transaction.CommitAsync(ct); return; }
        async Task Execute(string sql, object args) => await db.ExecuteAsync(new CommandDefinition(sql, args, transaction, cancellationToken: ct));
        await Execute("INSERT INTO Users(Id,DisplayName,Email) VALUES(@Id,@DisplayName,@Email)", DemoData.User);
        foreach (var product in DemoData.Products)
            await Execute("INSERT INTO Products(Id,Symbol,Name,Market,AssetType,IsLeveraged,QuantityUnit,UnitSize) VALUES(@Id,@Symbol,@Name,@Market,@AssetType,@IsLeveraged,@QuantityUnit,@UnitSize)", product);
        foreach (var provider in DemoData.Providers)
            await Execute("INSERT INTO AIProviders(Id,Code,DisplayName,SortOrder) VALUES(@Id,@Code,@DisplayName,@SortOrder)", provider);
        foreach (var product in DemoData.Products.Take(3))
            await Execute("INSERT INTO UserWatchlists(UserId,ProductId) VALUES(1,@Id)", product);
        foreach (var record in DemoData.History(DateTime.UtcNow))
            await Execute("""
                INSERT INTO AIAnalysisResults(UserId,ProductId,AIProviderId,Model,Action,Quantity,Confidence,AnalysisJson,InputSnapshotJson,RawResponse,CreatedAt)
                VALUES(@UserId,@ProductId,@AIProviderId,@Model,@Action,@Quantity,@Confidence,@AnalysisJson,@InputSnapshotJson,@RawResponse,@CreatedAt)
                """, new { record.UserId, record.ProductId, record.AIProviderId, record.Model, record.Result.Action,
                    record.Result.Quantity, record.Result.Confidence, AnalysisJson = JsonSerializer.Serialize(record.Result),
                    record.InputSnapshotJson, record.RawResponse, record.CreatedAt });
        await transaction.CommitAsync(ct);
    }

    private sealed class StoredAnalysis
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long ProductId { get; set; }
        public long AIProviderId { get; set; }
        public string Model { get; set; } = "";
        public string AnalysisJson { get; set; } = "";
        public string InputSnapshotJson { get; set; } = "";
        public string RawResponse { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? CachedTokens { get; set; }
    }
}
