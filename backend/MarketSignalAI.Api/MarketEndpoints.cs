using FastEndpoints;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Api;

public sealed record MarketResponse(string Symbol, decimal Price, decimal Open, decimal High, decimal Low,
    decimal PreviousClose, long Volume, DateTimeOffset MarketTime, DateTimeOffset FetchedAt);

public sealed class MarketEndpoint(SignalService service, IMarketStore market) : Endpoint<ProductRequest, MarketResponse>
{
    public override void Configure() { Get("/api/products/{symbol}/market"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductRequest req, CancellationToken ct)
    {
        var product = await service.RequireProductAsync(req.Symbol, req.Market, ct);
        var stored = await market.GetLatestSnapshotAsync(product.Id, ct);
        if (stored is null) { await SendNotFoundAsync(ct); return; }
        var s = stored.Snapshot;
        await SendAsync(new(product.Symbol, s.Price, s.Open, s.High, s.Low, s.PreviousClose,
            s.Volume, s.MarketTime, s.FetchedAt), cancellation: ct);
    }
}

public sealed class ForceAnalysisEndpoint(IMarketAnalysisRunner runner) : EndpointWithoutRequest<ProductRunResult>
{
    public override void Configure() { Post("/api/products/{symbol}/analysis/force"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(await runner.RunProductAsync(Route<string>("symbol") ?? throw new ArgumentException("缺少商品代碼。"), ct), cancellation: ct);
    }
}
