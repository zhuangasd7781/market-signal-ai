using FastEndpoints;
using MarketSignalAI.Application;
namespace MarketSignalAI.Api;
public sealed class AIProviderSettingsEndpoint(AIProviderSettingsService service) : EndpointWithoutRequest<IReadOnlyList<AIProviderSettingView>>
{
    public override void Configure(){Get("/api/ai/settings");AllowAnonymous();}
    public override async Task HandleAsync(CancellationToken ct)=>await SendAsync(await service.GetAsync(ct),cancellation:ct);
}
public sealed class SaveAIProviderSettingRequest { public bool? Enabled{get;set;} public string ConfiguredModel{get;set;}=""; }
public sealed class SaveAIProviderSettingEndpoint(AIProviderSettingsService service) : Endpoint<SaveAIProviderSettingRequest>
{
    public override void Configure(){Put("/api/ai/settings/{provider}");AllowAnonymous();}
    public override async Task HandleAsync(SaveAIProviderSettingRequest req,CancellationToken ct)
    {
        if(req.Enabled is null)throw new ArgumentException("Enabled is required.");
        await service.SaveAsync(new(Route<string>("provider") ?? "",req.Enabled.Value,req.ConfiguredModel),ct);
        await SendNoContentAsync(ct);
    }
}
