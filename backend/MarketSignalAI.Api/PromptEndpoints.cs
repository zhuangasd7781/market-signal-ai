using FastEndpoints;
using MarketSignalAI.Application;
namespace MarketSignalAI.Api;

public sealed class PromptSettingsEndpoint(PromptService service) : EndpointWithoutRequest<PromptSettings>
{
    public override void Configure() { Get("/api/prompts"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(await service.GetAsync(ct), cancellation: ct);
}
public sealed class CreatePromptRequest { public string Content { get; set; } = ""; }
public sealed class CreatePromptEndpoint(PromptService service) : Endpoint<CreatePromptRequest, PromptVersion>
{
    public override void Configure() { Post("/api/prompts"); AllowAnonymous(); }
    public override async Task HandleAsync(CreatePromptRequest request, CancellationToken ct) =>
        await SendAsync(await service.CreateAsync(request.Content, ct), 201, ct);
}
public sealed class ActivatePromptRequest { public long VersionId { get; set; } }
public sealed class ActivatePromptEndpoint(PromptService service) : Endpoint<ActivatePromptRequest>
{
    public override void Configure() { Put("/api/prompts/active"); AllowAnonymous(); }
    public override async Task HandleAsync(ActivatePromptRequest request, CancellationToken ct)
    {
        await service.ActivateAsync(request.VersionId, ct); await SendNoContentAsync(ct);
    }
}
