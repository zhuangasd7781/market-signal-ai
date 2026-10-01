using MarketSignalAI.Application;
using MarketSignalAI.Domain;
namespace MarketSignalAI.Infrastructure;

// Lifetime is exactly one Force request or RunAll batch, including unavailable-data outcomes.
internal sealed class ReferenceDataCache(IMarketDataProvider market)
{
    private readonly Dictionary<string,Task<MarketSnapshot>> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<(string,DateOnly,DateOnly),Task<IReadOnlyList<HistoricalPrice>>> histories = new();
    public async Task<MarketReferenceContext> GetAsync(ProductMarketReference mapping,DateOnly from,DateOnly through,CancellationToken ct)
    {
        var instrument=mapping.Instrument;MarketSnapshot? snapshot=null;IReadOnlyList<HistoricalPrice> history=[];var errors=new List<string>();
        try
        {
            if(!snapshots.TryGetValue(instrument.Symbol,out var task)) snapshots[instrument.Symbol]=task=FetchSnapshotAsync(instrument.Symbol,ct);
            snapshot=await task;
            if(snapshot.Symbol!=instrument.Symbol) throw new InvalidDataException("Reference symbol mismatch.");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){snapshot=null;errors.Add(SafeError("quote",ex));}
        try
        {
            var key=(instrument.Symbol,from,through);
            if(!histories.TryGetValue(key,out var task)) histories[key]=task=FetchHistoryAsync(instrument.Symbol,from,through,ct);
            history=await task;
            if(history.Count<2) errors.Add($"Yahoo supplied only {history.Count} daily bars; trend evidence is insufficient.");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){errors.Add(SafeError("history",ex));}
        if(snapshot is not null && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(snapshot.MarketTime,TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei")).DateTime)!=through)
            errors.Add("Reference quote date differs from the target quote date.");
        return new(mapping.Id,mapping.ReferenceType,instrument.Symbol,instrument.Name,instrument.Market,snapshot,history,
            snapshot is null ? "UNAVAILABLE" : errors.Count>0 ? "PARTIAL" : "AVAILABLE",errors.Count>0 ? string.Join(" ",errors) : null);
    }
    private async Task<MarketSnapshot> FetchSnapshotAsync(string ticker,CancellationToken ct)=>await market.GetReferenceSnapshotAsync(ticker,ct);
    private async Task<IReadOnlyList<HistoricalPrice>> FetchHistoryAsync(string ticker,DateOnly from,DateOnly through,CancellationToken ct)=>await market.GetReferenceHistoricalPricesAsync(ticker,from,through,ct);
    private static string SafeError(string part,Exception ex)=>ex is HttpRequestException { StatusCode: { } status } ? $"Yahoo reference {part} returned HTTP {(int)status}." : $"Yahoo reference {part} unavailable ({ex.GetType().Name}).";
}
