using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;

public sealed class MemoryMarketReferenceStore : IMarketReferenceStore
{
    private readonly object gate = new();
    private readonly List<ReferenceInstrument> instruments = [];
    private readonly List<(long Id,long UserId,long ProductId,long InstrumentId,string Type)> mappings = [];
    private long instrumentId, mappingId;
    public Task<IReadOnlyList<ReferenceInstrument>> GetInstrumentsAsync(long userId, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); lock(gate) return Task.FromResult<IReadOnlyList<ReferenceInstrument>>(instruments.Where(x=>x.UserId==userId).ToArray()); }
    public Task<ReferenceInstrument> SaveInstrumentAsync(long userId, long? id, string symbol, string name, string market, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); MarketReferenceValidation.Instrument(symbol,name,market);
        lock(gate)
        {
            var existing = id is null ? null : instruments.SingleOrDefault(x=>x.Id==id && x.UserId==userId) ?? throw new KeyNotFoundException("Reference instrument not found.");
            if(instruments.Any(x=>x.UserId==userId && x.Symbol==symbol && x.Id!=id)) throw new InvalidOperationException("Reference instrument already exists.");
            var result = new ReferenceInstrument(existing?.Id ?? ++instrumentId,userId,symbol,name,market);
            if(existing is not null) instruments.Remove(existing); instruments.Add(result); return Task.FromResult(result);
        }
    }
    public Task DeleteInstrumentAsync(long userId, long id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); lock(gate)
        {
            var existing=instruments.SingleOrDefault(x=>x.Id==id && x.UserId==userId) ?? throw new KeyNotFoundException("Reference instrument not found.");
            if(mappings.Any(x=>x.InstrumentId==id)) throw new InvalidOperationException("Remove product mappings before deleting this instrument.");
            instruments.Remove(existing); return Task.CompletedTask;
        }
    }
    public Task<IReadOnlyList<ProductMarketReference>> GetReferencesAsync(long userId,long productId,CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); lock(gate) return Task.FromResult<IReadOnlyList<ProductMarketReference>>(mappings.Where(x=>x.UserId==userId && x.ProductId==productId).OrderBy(x=>x.Id).Select(x=>new ProductMarketReference(x.Id,x.UserId,x.ProductId,x.Type,instruments.Single(i=>i.Id==x.InstrumentId))).ToArray()); }
    public Task<ProductMarketReference> SaveReferenceAsync(long userId,long productId,long? id,long instrumentId,string referenceType,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); MarketReferenceValidation.Type(referenceType); lock(gate)
        {
            var instrument=instruments.SingleOrDefault(x=>x.Id==instrumentId && x.UserId==userId) ?? throw new KeyNotFoundException("Reference instrument not found.");
            if(id is not null && !mappings.Any(x=>x.Id==id && x.UserId==userId && x.ProductId==productId)) throw new KeyNotFoundException("Product reference not found.");
            if(mappings.Any(x=>x.UserId==userId && x.ProductId==productId && x.InstrumentId==instrumentId && x.Type==referenceType && x.Id!=id)) throw new InvalidOperationException("Product reference already exists.");
            var result=new ProductMarketReference(id ?? ++mappingId,userId,productId,referenceType,instrument);
            mappings.RemoveAll(x=>x.Id==result.Id); mappings.Add((result.Id,userId,productId,instrumentId,referenceType)); return Task.FromResult(result);
        }
    }
    public Task DeleteReferenceAsync(long userId,long productId,long id,CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); lock(gate) { if(mappings.RemoveAll(x=>x.Id==id && x.UserId==userId && x.ProductId==productId)==0) throw new KeyNotFoundException("Product reference not found."); return Task.CompletedTask; } }
}
