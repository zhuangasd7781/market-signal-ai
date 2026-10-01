using FastEndpoints;
using System.Text.Json;
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

public sealed class ForceAnalysisRequest { public string[]? Providers { get; set; } }
public sealed class ForceAnalysisEndpoint(IMarketAnalysisRunner runner) : EndpointWithoutRequest<ProductRunResult>
{
    public override void Configure() { Post("/api/products/{symbol}/analysis/force"); AllowAnonymous(); Description(b=>b.Accepts<ForceAnalysisRequest>(true,"application/json","*/*")); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        ForceAnalysisRequest? req=null;
        if(HttpContext.Request.ContentLength is > 0 || HttpContext.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            if(!HttpContext.Request.HasJsonContentType())throw new ArgumentException("Force request body must use application/json.");
            try { req=await HttpContext.Request.ReadFromJsonAsync<ForceAnalysisRequest>(ct) ?? throw new ArgumentException("Force request must be a JSON object."); }
            catch(JsonException) { throw new ArgumentException("Force request must be valid JSON with a providers array."); }
        }
        await SendAsync(await runner.RunProductAsync(Route<string>("symbol") ?? throw new ArgumentException("缺少商品代碼。"), req?.Providers, ct), cancellation: ct);
    }
}
