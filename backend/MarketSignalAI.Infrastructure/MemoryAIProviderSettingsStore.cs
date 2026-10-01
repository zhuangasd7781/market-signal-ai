using MarketSignalAI.Application;
namespace MarketSignalAI.Infrastructure;
public sealed class MemoryAIProviderSettingsStore(IReadOnlyList<AIProviderSetting> defaults) : IAIProviderSettingsStore
{
    private readonly object gate=new();
    private readonly Dictionary<(long,string),AIProviderSetting> saved=new();
    public Task<IReadOnlyList<AIProviderSetting>> GetAsync(long userId,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();lock(gate)return Task.FromResult<IReadOnlyList<AIProviderSetting>>(defaults.Select(x=>saved.GetValueOrDefault((userId,x.Provider),x)).ToArray());}
    public Task SaveAsync(long userId,AIProviderSetting setting,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();AIProviderSettingsValidation.Validate(setting);lock(gate)saved[(userId,setting.Provider)]=setting;return Task.CompletedTask;}
}
