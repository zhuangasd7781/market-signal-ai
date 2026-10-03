using FastEndpoints;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Api;
public sealed class RefreshNewsEndpoint(INewsContextRefresher service) : EndpointWithoutRequest<NewsContext>
{
    public override void Configure(){Post("/api/news-context/refresh");AllowAnonymous();}
    public override async Task HandleAsync(CancellationToken ct)=>await SendAsync(await service.RefreshAsync(ct),201,ct);
}
public sealed class LatestNewsEndpoint(INewsContextStore store) : EndpointWithoutRequest<NewsContext>
{
    public override void Configure(){Get("/api/news-context/latest");AllowAnonymous();}
    public override async Task HandleAsync(CancellationToken ct)=>await SendAsync(await store.GetLatestAsync(ct) ?? throw new KeyNotFoundException("尚未建立市場情報，請立即更新。"),cancellation:ct);
}
public sealed class NewsByIdRequest {public long ContextId {get;set;}}
public sealed class NewsByIdEndpoint(INewsContextStore store) : Endpoint<NewsByIdRequest,NewsContext>
{
    public override void Configure(){Get("/api/news-context/{contextId:long}");AllowAnonymous();}
    public override async Task HandleAsync(NewsByIdRequest request,CancellationToken ct)=>await SendAsync(await store.GetAsync(Route<long>("contextId"),ct) ?? throw new KeyNotFoundException("找不到這筆市場情報。"),cancellation:ct);
}
