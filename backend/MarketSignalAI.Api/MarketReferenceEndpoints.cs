using FastEndpoints;
using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Api;

public class InstrumentRequest
{
    public long InstrumentId {get;set;}
    public string Symbol {get;set;}="";
    public string Name {get;set;}="";
    public string Market {get;set;}="";
}
public class ReferenceRequest : ProductRequest
{
    public long ReferenceId {get;set;}
    public long InstrumentId {get;set;}
    public string ReferenceType {get;set;}="";
}
public sealed class ReferenceInstrumentsEndpoint(IMarketReferenceStore store,ICurrentUser user) : EndpointWithoutRequest<IReadOnlyList<ReferenceInstrument>>
{
    public override void Configure(){Get("/api/market-reference-instruments");AllowAnonymous();}
    public override async Task HandleAsync(CancellationToken ct)=>await SendAsync(await store.GetInstrumentsAsync(user.UserId,ct),cancellation:ct);
}
public sealed class CreateReferenceInstrumentEndpoint(IMarketReferenceStore store,ICurrentUser user) : Endpoint<InstrumentRequest,ReferenceInstrument>
{
    public override void Configure(){Post("/api/market-reference-instruments");AllowAnonymous();}
    public override async Task HandleAsync(InstrumentRequest req,CancellationToken ct)=>await SendAsync(await store.SaveInstrumentAsync(user.UserId,null,req.Symbol,req.Name,req.Market,ct),201,ct);
}
public sealed class UpdateReferenceInstrumentEndpoint(IMarketReferenceStore store,ICurrentUser user) : Endpoint<InstrumentRequest,ReferenceInstrument>
{
    public override void Configure(){Put("/api/market-reference-instruments/{instrumentId}");AllowAnonymous();}
    public override async Task HandleAsync(InstrumentRequest req,CancellationToken ct)=>await SendAsync(await store.SaveInstrumentAsync(user.UserId,Route<long>("instrumentId"),req.Symbol,req.Name,req.Market,ct),cancellation:ct);
}
public sealed class DeleteReferenceInstrumentEndpoint(IMarketReferenceStore store,ICurrentUser user) : Endpoint<InstrumentRequest>
{
    public override void Configure(){Delete("/api/market-reference-instruments/{instrumentId}");AllowAnonymous();}
    public override async Task HandleAsync(InstrumentRequest req,CancellationToken ct){await store.DeleteInstrumentAsync(user.UserId,Route<long>("instrumentId"),ct);await SendNoContentAsync(ct);}
}
public sealed class ProductReferencesEndpoint(IMarketReferenceStore store,SignalService signals,ICurrentUser user) : Endpoint<ProductRequest,IReadOnlyList<ProductMarketReference>>
{
    public override void Configure(){Get("/api/products/{symbol}/references");AllowAnonymous();}
    public override async Task HandleAsync(ProductRequest req,CancellationToken ct)
    {var product=await signals.RequireProductAsync(Route<string>("symbol")!,req.Market,ct);await SendAsync(await store.GetReferencesAsync(user.UserId,product.Id,ct),cancellation:ct);}
}
public sealed class CreateProductReferenceEndpoint(IMarketReferenceStore store,SignalService signals,ICurrentUser user) : Endpoint<ReferenceRequest,ProductMarketReference>
{
    public override void Configure(){Post("/api/products/{symbol}/references");AllowAnonymous();}
    public override async Task HandleAsync(ReferenceRequest req,CancellationToken ct)
    {var product=await signals.RequireProductAsync(Route<string>("symbol")!,req.Market,ct);await SendAsync(await store.SaveReferenceAsync(user.UserId,product.Id,null,req.InstrumentId,req.ReferenceType,ct),201,ct);}
}
public sealed class UpdateProductReferenceEndpoint(IMarketReferenceStore store,SignalService signals,ICurrentUser user) : Endpoint<ReferenceRequest,ProductMarketReference>
{
    public override void Configure(){Put("/api/products/{symbol}/references/{referenceId}");AllowAnonymous();}
    public override async Task HandleAsync(ReferenceRequest req,CancellationToken ct)
    {var product=await signals.RequireProductAsync(Route<string>("symbol")!,req.Market,ct);await SendAsync(await store.SaveReferenceAsync(user.UserId,product.Id,Route<long>("referenceId"),req.InstrumentId,req.ReferenceType,ct),cancellation:ct);}
}
public sealed class DeleteProductReferenceEndpoint(IMarketReferenceStore store,SignalService signals,ICurrentUser user) : Endpoint<ReferenceRequest>
{
    public override void Configure(){Delete("/api/products/{symbol}/references/{referenceId}");AllowAnonymous();}
    public override async Task HandleAsync(ReferenceRequest req,CancellationToken ct)
    {var product=await signals.RequireProductAsync(Route<string>("symbol")!,req.Market,ct);await store.DeleteReferenceAsync(user.UserId,product.Id,Route<long>("referenceId"),ct);await SendNoContentAsync(ct);}
}
