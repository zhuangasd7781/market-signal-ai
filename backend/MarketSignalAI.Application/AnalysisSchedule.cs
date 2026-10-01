namespace MarketSignalAI.Application;
public sealed record AnalysisSchedule(bool Enabled, IReadOnlyList<string> Times)
{
    public static AnalysisSchedule Default => new(true, ["09:05", "10:05", "12:05", "13:05"]);
    public static int Minute(string time)
    {
        if (!TimeOnly.TryParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Time must use HH:mm (Asia/Taipei).");
        return parsed.Hour * 60 + parsed.Minute;
    }
    public void Validate()
    {
        if (Times is null || Times.Count > 24) throw new ArgumentException("At most 24 analysis times are allowed.");
        foreach (var time in Times) Minute(time);
        if (Times.Distinct().Count() != Times.Count) throw new ArgumentException("Duplicate analysis times are not allowed.");
    }
}
public interface IAnalysisScheduleStore
{
    Task<AnalysisSchedule> GetAsync(long userId, CancellationToken ct);
    Task SaveAsync(long userId, AnalysisSchedule schedule, CancellationToken ct);
    Task<bool> TryClaimAsync(long userId, DateOnly date, int minute, CancellationToken ct);
}
