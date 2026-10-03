using Dapper;
using MySqlConnector;
using MarketSignalAI.Application;
namespace MarketSignalAI.Infrastructure;
public sealed class MemoryAnalysisExecutionSettingsStore : IAnalysisExecutionSettingsStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, AnalysisExecutionSettings> rows = new();
    public Task<AnalysisExecutionSettings> GetAsync(long userId, CancellationToken ct) => Task.FromResult(rows.GetValueOrDefault(userId, new()));
    public Task SaveAsync(long userId, AnalysisExecutionSettings settings, CancellationToken ct) { rows[userId] = settings; return Task.CompletedTask; }
}
public sealed class MySqlAnalysisExecutionSettingsStore(string connectionString) : IAnalysisExecutionSettingsStore
{
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        using var stream = typeof(MySqlAnalysisExecutionSettingsStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.011_analysis_execution_settings.sql")!;
        using var reader = new StreamReader(stream);
        await db.ExecuteAsync(new CommandDefinition(await reader.ReadToEndAsync(ct), cancellationToken: ct));
    }
    public async Task<AnalysisExecutionSettings> GetAsync(long userId, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        var value = await db.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition("SELECT RefreshNewsBeforeAnalysis FROM AnalysisExecutionSettings WHERE UserId=@userId", new { userId }, cancellationToken: ct));
        return new(value ?? false);
    }
    public async Task SaveAsync(long userId, AnalysisExecutionSettings settings, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO AnalysisExecutionSettings(UserId,RefreshNewsBeforeAnalysis) VALUES(@userId,@RefreshNewsBeforeAnalysis) ON DUPLICATE KEY UPDATE RefreshNewsBeforeAnalysis=@RefreshNewsBeforeAnalysis", new { userId, settings.RefreshNewsBeforeAnalysis }, cancellationToken: ct));
    }
}
