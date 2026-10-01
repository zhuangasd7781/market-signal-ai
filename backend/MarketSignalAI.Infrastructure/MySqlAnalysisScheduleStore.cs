using System.Text.Json;
using Dapper;
using MarketSignalAI.Application;
using MySqlConnector;
namespace MarketSignalAI.Infrastructure;
public sealed class MySqlAnalysisScheduleStore(string connectionString) : IAnalysisScheduleStore
{
    public async Task MigrateAndSeedAsync(CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        using var stream = typeof(MySqlAnalysisScheduleStore).Assembly.GetManifestResourceStream("MarketSignalAI.Infrastructure.Migrations.006_analysis_schedule_settings.sql")!;
        using var reader = new StreamReader(stream);
        foreach(var sql in (await reader.ReadToEndAsync(ct)).Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            await db.ExecuteAsync(new CommandDefinition(sql,cancellationToken:ct));
        await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO AnalysisScheduleSettings(UserId,Enabled,TimesJson) VALUES(1,true,@times)",new { times=JsonSerializer.Serialize(AnalysisSchedule.Default.Times) },cancellationToken:ct));
    }
    public async Task<AnalysisSchedule> GetAsync(long userId,CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        var row=await db.QuerySingleOrDefaultAsync<Stored>(new CommandDefinition("SELECT Enabled,TimesJson FROM AnalysisScheduleSettings WHERE UserId=@userId",new{userId},cancellationToken:ct));
        return row is null ? AnalysisSchedule.Default : new(row.Enabled,JsonSerializer.Deserialize<string[]>(row.TimesJson)!);
    }
    public async Task SaveAsync(long userId,AnalysisSchedule schedule,CancellationToken ct)
    {
        schedule.Validate(); await using var db=new MySqlConnection(connectionString);
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO AnalysisScheduleSettings(UserId,Enabled,TimesJson) VALUES(@userId,@Enabled,@times) ON DUPLICATE KEY UPDATE Enabled=@Enabled,TimesJson=@times",new{userId,schedule.Enabled,times=JsonSerializer.Serialize(schedule.Times.Order())},cancellationToken:ct));
    }
    public async Task<bool> TryClaimAsync(long userId,DateOnly date,int minute,CancellationToken ct)
    {
        await using var db=new MySqlConnection(connectionString);
        return await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO AnalysisScheduleClaims(UserId,TradeDate,MinuteOfDay) VALUES(@userId,@date,@minute)",new{userId,date=date.ToDateTime(TimeOnly.MinValue),minute},cancellationToken:ct))==1;
    }
    private sealed record Stored(bool Enabled,string TimesJson);
}
