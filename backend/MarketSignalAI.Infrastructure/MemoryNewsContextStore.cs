using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;
public sealed class MemoryNewsContextStore : INewsContextStore
{
    private readonly object gate = new();
    private readonly List<NewsContext> contexts = [];
    public List<NewsRefreshFailure> Failures { get; } = [];
    public Dictionary<long,NewsRefreshAudit> Audits { get; } = [];
    public Task<NewsContext> AppendAsync(NewsContext context, NewsRefreshAudit audit, CancellationToken ct)
    { lock(gate) { var saved=context with { Id=contexts.Count+1 };contexts.Add(saved);Audits.Add(saved.Id,audit);return Task.FromResult(saved); } }
    public Task<NewsContext?> GetLatestValidAsync(CancellationToken ct) { lock(gate) return Task.FromResult(contexts.LastOrDefault(x=>x.Status is "AVAILABLE" or "PARTIAL")); }
    public Task<NewsContext?> GetLatestAsync(CancellationToken ct) { lock(gate) return Task.FromResult(contexts.LastOrDefault()); }
    public Task<NewsContext?> GetAsync(long id,CancellationToken ct) { lock(gate) return Task.FromResult(contexts.SingleOrDefault(x=>x.Id==id)); }
    public Task AppendFailureAsync(NewsRefreshFailure failure,CancellationToken ct) { lock(gate) Failures.Add(failure);return Task.CompletedTask; }
}
