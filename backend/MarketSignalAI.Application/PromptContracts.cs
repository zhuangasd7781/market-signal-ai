namespace MarketSignalAI.Application;

public sealed record PromptVersion(long Id, string Version, string Content, DateTime CreatedAt);
public sealed record PromptSettings(long ActiveVersionId, IReadOnlyList<PromptVersion> Versions);
public sealed record SkillSnapshot(string Identifier, string Content);
public sealed record AnalysisPromptSnapshot(long Id, string Version, string Content, IReadOnlyList<SkillSnapshot> Skills);
public interface IPromptStore
{
    Task<PromptSettings> GetAsync(long userId, CancellationToken ct);
    Task<PromptVersion> CreateAsync(long userId, string content, CancellationToken ct);
    Task ActivateAsync(long userId, long versionId, CancellationToken ct);
}
public static class PromptValidation
{
    public static void Content(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 30000)
            throw new ArgumentException("Prompt content must contain 1 to 30000 characters.");
    }
}
public sealed class PromptService(IPromptStore store, ICurrentUser user)
{
    public Task<PromptSettings> GetAsync(CancellationToken ct) => store.GetAsync(user.UserId, ct);
    public Task<PromptVersion> CreateAsync(string content, CancellationToken ct)
    {
        PromptValidation.Content(content);
        return store.CreateAsync(user.UserId, content, ct);
    }
    public Task ActivateAsync(long versionId, CancellationToken ct) => store.ActivateAsync(user.UserId, versionId, ct);
}
