using Dapper;
using MarketSignalAI.Application;
using MySqlConnector;
namespace MarketSignalAI.Infrastructure;
public sealed class MySqlAIProviderSettingsStore(string connectionString,IReadOnlyList<AIProviderSetting> defaults) : IAIProviderSettingsStore
{
    public async Task MigrateAndSeedAsync(CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        using var stream=typeof(MySqlAIProviderSettingsStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.005_ai_provider_settings.sql") ?? throw new InvalidOperationException("Provider settings migration missing.");
        using var reader=new StreamReader(stream);
        foreach(var sql in (await reader.ReadToEndAsync(ct)).Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            await db.ExecuteAsync(new CommandDefinition(sql,cancellationToken:ct));
        foreach(var row in defaults)
            await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO AIProviderSettings(UserId,Provider,Enabled,ConfiguredModel) VALUES(1,@Provider,@Enabled,@ConfiguredModel)",row,cancellationToken:ct));
    }
    public async Task<IReadOnlyList<AIProviderSetting>> GetAsync(long userId,CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        var rows=(await db.QueryAsync<AIProviderSetting>(new CommandDefinition("SELECT Provider,Enabled,ConfiguredModel FROM AIProviderSettings WHERE UserId=@userId",new {userId},cancellationToken:ct))).ToDictionary(x=>x.Provider);
        return defaults.Select(x=>rows.GetValueOrDefault(x.Provider,x)).ToArray();
    }
    public async Task SaveAsync(long userId,AIProviderSetting row,CancellationToken ct)
    {
        AIProviderSettingsValidation.Validate(row);
        await using var db=new MySqlConnection(connectionString);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO AIProviderSettings(UserId,Provider,Enabled,ConfiguredModel) VALUES(@userId,@Provider,@Enabled,@ConfiguredModel) ON DUPLICATE KEY UPDATE Enabled=@Enabled,ConfiguredModel=@ConfiguredModel",new {userId,row.Provider,row.Enabled,row.ConfiguredModel},cancellationToken:ct));
    }
}
