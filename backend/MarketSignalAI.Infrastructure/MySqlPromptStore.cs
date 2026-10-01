using Dapper;
using MarketSignalAI.Application;
using MySqlConnector;
namespace MarketSignalAI.Infrastructure;

public sealed class MySqlPromptStore(string connectionString) : IPromptStore
{
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        using var stream = typeof(MySqlPromptStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.007_prompt_versions.sql")
            ?? throw new InvalidOperationException("Prompt migration missing.");
        using var reader = new StreamReader(stream);
        foreach (var sql in (await reader.ReadToEndAsync(ct)).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await db.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
        await EnsureAsync(db, 1, ct);
    }
    private static async Task EnsureAsync(MySqlConnection db, long userId, CancellationToken ct)
    {
        await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO PromptVersions(UserId,VersionNumber,Content,CreatedAt) VALUES(@userId,1,@content,UTC_TIMESTAMP(6))",
            new { userId, content = AnalysisProtocol.CommonInstructions }, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO UserActivePrompts(UserId,PromptVersionId) SELECT UserId,Id FROM PromptVersions WHERE UserId=@userId AND VersionNumber=1",
            new { userId }, cancellationToken: ct));
    }
    public async Task<PromptSettings> GetAsync(long userId, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await EnsureAsync(db, userId, ct);
        var selected = await db.QuerySingleAsync<long>(new CommandDefinition("SELECT PromptVersionId FROM UserActivePrompts WHERE UserId=@userId", new { userId }, cancellationToken: ct));
        var rows = await db.QueryAsync<PromptVersion>(new CommandDefinition("SELECT Id,CONCAT('investment-analysis-v',VersionNumber) AS Version,Content,CreatedAt FROM PromptVersions WHERE UserId=@userId ORDER BY VersionNumber DESC", new { userId }, cancellationToken: ct));
        return new(selected, rows.ToArray());
    }
    public async Task<PromptVersion> CreateAsync(long userId, string content, CancellationToken ct)
    {
        PromptValidation.Content(content);
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct); await EnsureAsync(db, userId, ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.QuerySingleAsync<long>(new CommandDefinition("SELECT PromptVersionId FROM UserActivePrompts WHERE UserId=@userId FOR UPDATE", new { userId }, transaction, cancellationToken: ct));
        var number = await db.QuerySingleAsync<int>(new CommandDefinition("SELECT MAX(VersionNumber)+1 FROM PromptVersions WHERE UserId=@userId", new { userId }, transaction, cancellationToken: ct));
        var createdAt = DateTime.UtcNow;
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO PromptVersions(UserId,VersionNumber,Content,CreatedAt) VALUES(@userId,@number,@content,@createdAt)", new { userId, number, content, createdAt }, transaction, cancellationToken: ct));
        var id = await db.QuerySingleAsync<long>(new CommandDefinition("SELECT LAST_INSERT_ID()", transaction: transaction, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("UPDATE UserActivePrompts SET PromptVersionId=@id WHERE UserId=@userId", new { userId, id }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return new(id, $"investment-analysis-v{number}", content, createdAt);
    }
    public async Task ActivateAsync(long userId, long versionId, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await EnsureAsync(db, userId, ct);
        var exists = await db.QuerySingleAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM PromptVersions WHERE UserId=@userId AND Id=@versionId", new { userId, versionId }, cancellationToken: ct));
        if (exists != 1) throw new KeyNotFoundException("Prompt version was not found.");
        await db.ExecuteAsync(new CommandDefinition("UPDATE UserActivePrompts SET PromptVersionId=@versionId WHERE UserId=@userId", new { userId, versionId }, cancellationToken: ct));
    }
}
