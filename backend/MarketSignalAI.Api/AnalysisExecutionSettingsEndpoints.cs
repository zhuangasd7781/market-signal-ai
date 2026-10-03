using FastEndpoints;
using MarketSignalAI.Application;
namespace MarketSignalAI.Api;
public sealed class AnalysisExecutionSettingsEndpoint(IAnalysisExecutionSettingsStore store, ICurrentUser user) : EndpointWithoutRequest<AnalysisExecutionSettings>
{
    public override void Configure() { Get("/api/ai/analysis-settings"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(await store.GetAsync(user.UserId, ct), cancellation: ct);
}
public sealed class SaveAnalysisExecutionSettingsRequest { public bool? RefreshNewsBeforeAnalysis { get; set; } }
public sealed class SaveAnalysisExecutionSettingsEndpoint(IAnalysisExecutionSettingsStore store, ICurrentUser user) : Endpoint<SaveAnalysisExecutionSettingsRequest>
{
    public override void Configure() { Put("/api/ai/analysis-settings"); AllowAnonymous(); }
    public override async Task HandleAsync(SaveAnalysisExecutionSettingsRequest req, CancellationToken ct)
    {
        if (req.RefreshNewsBeforeAnalysis is null) throw new ArgumentException("RefreshNewsBeforeAnalysis is required.");
        await store.SaveAsync(user.UserId, new(req.RefreshNewsBeforeAnalysis.Value), ct);
        await SendNoContentAsync(ct);
    }
}
