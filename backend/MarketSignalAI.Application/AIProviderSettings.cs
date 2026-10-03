using System.Text.RegularExpressions;
namespace MarketSignalAI.Application;

public sealed record AIProviderSetting(string Provider, bool Enabled, string ConfiguredModel, bool Visible = true);
public sealed record AIProviderSettingView(string Provider, string DisplayName, bool Enabled, string ConfiguredModel, string? ActualModel, bool IsMock, bool Visible = true);
public interface IAIProviderSettingsStore
{
    Task<IReadOnlyList<AIProviderSetting>> GetAsync(long userId, CancellationToken ct);
    Task SaveAsync(long userId, AIProviderSetting setting, CancellationToken ct);
}
public static class AIProviderSettingsValidation
{
    public static void Validate(AIProviderSetting setting)
    {
        if (setting.Provider is not ("openai" or "deepseek" or "claude")) throw new ArgumentException("Unknown AI provider.");
        if (string.IsNullOrWhiteSpace(setting.ConfiguredModel) || setting.ConfiguredModel.Length > 100 ||
            !Regex.IsMatch(setting.ConfiguredModel, "^[A-Za-z0-9][A-Za-z0-9_.:/-]*$")) throw new ArgumentException("Model must be a valid model ID of at most 100 characters.");
        if (setting.Provider == "claude" ? setting.ConfiguredModel != "mock-v1" : setting.ConfiguredModel.StartsWith("mock", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Claude currently supports mock-v1 only; OpenAI and DeepSeek require a real model ID.");
    }
}
public sealed class AIProviderSettingsService(IAIProviderSettingsStore settings, ISignalStore signals, ICurrentUser user)
{
    public async Task<IReadOnlyList<AIProviderSettingView>> GetAsync(CancellationToken ct)
    {
        var rows=await settings.GetAsync(user.UserId,ct);
        var providers=await signals.GetProvidersAsync(ct);
        var history=await signals.GetHistoryAsync(user.UserId,null,ct);
        return providers.Select(p =>
        {
            var row=rows.Single(x=>x.Provider==p.Code);
            var actual=history.Where(x=>x.AIProviderId==p.Id).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).FirstOrDefault()?.Model;
            return new AIProviderSettingView(p.Code,p.DisplayName,row.Enabled,row.ConfiguredModel,actual,p.Code=="claude",row.Visible);
        }).ToArray();
    }
    public async Task UpdateAsync(string provider, bool enabled, string model, bool? visible, CancellationToken ct)
    {
        var row = new AIProviderSetting(provider, enabled, model, visible ?? true);
        AIProviderSettingsValidation.Validate(row);
        if (visible is null)
        {
            var current = await settings.GetAsync(user.UserId, ct);
            row = row with { Visible = current.Single(x => x.Provider == provider).Visible };
        }
        await settings.SaveAsync(user.UserId, row, ct);
    }
    public Task SaveAsync(AIProviderSetting row,CancellationToken ct) { AIProviderSettingsValidation.Validate(row);return settings.SaveAsync(user.UserId,row,ct); }
}
