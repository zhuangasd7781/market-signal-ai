using MarketSignalAI.Application;
namespace MarketSignalAI.Infrastructure;

public sealed class MemoryPromptStore : IPromptStore
{
    private readonly object gate = new();
    private readonly Dictionary<long, List<PromptVersion>> versions = new();
    private readonly Dictionary<long, long> active = new();
    private long nextId;
    private void Ensure(long userId)
    {
        if (versions.ContainsKey(userId)) return;
        var seed = new PromptVersion(++nextId, "investment-analysis-v1", AnalysisProtocol.CommonInstructions, DateTime.UtcNow);
        versions[userId] = [seed]; active[userId] = seed.Id;
    }
    public Task<PromptSettings> GetAsync(long userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate) { Ensure(userId); return Task.FromResult(new PromptSettings(active[userId], versions[userId].AsEnumerable().Reverse().ToArray())); }
    }
    public Task<PromptVersion> CreateAsync(long userId, string content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); PromptValidation.Content(content);
        lock (gate)
        {
            Ensure(userId);
            var version = new PromptVersion(++nextId, $"investment-analysis-v{versions[userId].Count + 1}", content, DateTime.UtcNow);
            versions[userId].Add(version); active[userId] = version.Id; return Task.FromResult(version);
        }
    }
    public Task ActivateAsync(long userId, long versionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            Ensure(userId);
            if (!versions[userId].Any(x => x.Id == versionId)) throw new KeyNotFoundException("Prompt version was not found.");
            active[userId] = versionId;
        }
        return Task.CompletedTask;
    }
}
