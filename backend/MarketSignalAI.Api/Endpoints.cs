using FastEndpoints;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Api;

public sealed record MeResponse(User? User, bool IsMock);
public sealed class MeEndpoint(ISignalStore store, ICurrentUser user) : EndpointWithoutRequest<MeResponse>
{
    public override void Configure() { Get("/api/me"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(new(await store.GetUserAsync(user.UserId, ct), true), cancellation: ct);
}
public sealed class WatchlistEndpoint(SignalService service) : EndpointWithoutRequest<WatchlistResponse>
{
    public override void Configure() { Get("/api/watchlist"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(await service.GetWatchlistAsync(ct), cancellation: ct);
}
public sealed class ProductIdRequest { public long ProductId { get; set; } }
public sealed class AddWatchEndpoint(ISignalStore store, ICurrentUser user) : Endpoint<ProductIdRequest>
{
    public override void Configure() { Post("/api/watchlist"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductIdRequest req, CancellationToken ct)
    {
        await store.AddWatchAsync(user.UserId, req.ProductId, ct);
        await SendNoContentAsync(ct);
    }
}
public sealed class RemoveWatchEndpoint(ISignalStore store, ICurrentUser user) : Endpoint<ProductIdRequest>
{
    public override void Configure() { Delete("/api/watchlist/{productId}"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductIdRequest req, CancellationToken ct)
    {
        await store.RemoveWatchAsync(user.UserId, req.ProductId, ct);
        await SendNoContentAsync(ct);
    }
}
public sealed class SearchRequest { public string? Q { get; set; } }
public sealed class SearchEndpoint(ISignalStore store) : Endpoint<SearchRequest, IReadOnlyList<Product>>
{
    public override void Configure() { Get("/api/products/search"); AllowAnonymous(); }
    public override async Task HandleAsync(SearchRequest req, CancellationToken ct)
    {
        if (req.Q?.Length > 100) throw new ArgumentException("搜尋文字不得超過 100 字。");
        await SendAsync(await store.SearchProductsAsync(req.Q?.Trim() ?? "", ct), cancellation: ct);
    }
}
public class ProductRequest
{
    public string Symbol { get; set; } = "";
    public string Market { get; set; } = "TW";
}
public sealed class ProductEndpoint(SignalService service) : Endpoint<ProductRequest, Product>
{
    public override void Configure() { Get("/api/products/{symbol}"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductRequest req, CancellationToken ct) =>
        await SendAsync(await service.RequireProductAsync(req.Symbol, req.Market, ct), cancellation: ct);
}
public sealed class ProvidersEndpoint(ISignalStore store) : EndpointWithoutRequest<IReadOnlyList<AIProvider>>
{
    public override void Configure() { Get("/api/ai/providers"); AllowAnonymous(); }
    public override async Task HandleAsync(CancellationToken ct) => await SendAsync(await store.GetProvidersAsync(ct), cancellation: ct);
}
public sealed record PositionResponse(UserPosition? Position, object? Quote);
public sealed class PositionEndpoint(ISignalStore store, SignalService service, ICurrentUser user) : Endpoint<ProductRequest, PositionResponse>
{
    public override void Configure() { Get("/api/products/{symbol}/position"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductRequest req, CancellationToken ct)
    {
        var product = await service.RequireProductAsync(req.Symbol, req.Market, ct);
        await SendAsync(new(await store.GetPositionAsync(user.UserId, product.Id, ct), null), cancellation: ct);
    }
}
public sealed class SavePositionRequest : ProductRequest
{
    public decimal? Quantity { get; set; }
    public decimal? AverageCost { get; set; }
}
public sealed class SavePositionEndpoint(ISignalStore store, SignalService service, ICurrentUser user) : Endpoint<SavePositionRequest>
{
    public override void Configure() { Put("/api/products/{symbol}/position"); AllowAnonymous(); }
    public override async Task HandleAsync(SavePositionRequest req, CancellationToken ct)
    {
        if (req.Quantity is null or < 0 or > 999999999999m || req.AverageCost is null or < 0 or > 999999999999m)
            throw new ArgumentException("數量與平均成本須為 0 至 999999999999 的數字。");
        if (decimal.Round(req.Quantity.Value, 6) != req.Quantity || decimal.Round(req.AverageCost.Value, 6) != req.AverageCost)
            throw new ArgumentException("最多支援六位小數。");
        var product = await service.RequireProductAsync(req.Symbol, req.Market, ct);
        await store.SavePositionAsync(new(user.UserId, product.Id, req.Quantity.Value, req.AverageCost.Value, DateTime.UtcNow), ct);
        await SendNoContentAsync(ct);
    }
}
public sealed class AnalysisEndpoint(SignalService service) : Endpoint<ProductRequest, IReadOnlyList<AnalysisView>>
{
    public override void Configure() { Get("/api/products/{symbol}/analysis"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductRequest req, CancellationToken ct)
    {
        var product = await service.RequireProductAsync(req.Symbol, req.Market, ct);
        await SendAsync(await service.GetAnalysisAsync(product.Id, false, ct), cancellation: ct);
    }
}
public sealed class HistoryEndpoint(SignalService service) : Endpoint<ProductRequest, IReadOnlyList<AnalysisView>>
{
    public override void Configure() { Get("/api/products/{symbol}/analysis/history"); AllowAnonymous(); }
    public override async Task HandleAsync(ProductRequest req, CancellationToken ct)
    {
        var product = await service.RequireProductAsync(req.Symbol, req.Market, ct);
        await SendAsync(await service.GetAnalysisAsync(product.Id, true, ct), cancellation: ct);
    }
}
