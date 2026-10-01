namespace MarketSignalAI.Domain;

public sealed record ReferenceInstrument(long Id, long UserId, string Symbol, string Name, string Market);
public sealed record ProductMarketReference(long Id, long UserId, long ProductId, string ReferenceType, ReferenceInstrument Instrument);
public sealed record MarketReferenceContext(long MappingId, string ReferenceType, string Symbol, string Name, string Market,
    MarketSnapshot? Snapshot, IReadOnlyList<HistoricalPrice> History, string Status, string? Error)
{
    public decimal? CurrentValue => Snapshot?.Price;
    public decimal? Change => Snapshot is { } s ? s.Price - s.PreviousClose : null;
    public decimal? ChangePercent => Snapshot is { PreviousClose: > 0 } s ? decimal.Round((s.Price - s.PreviousClose) / s.PreviousClose * 100, 4) : null;
}
public sealed record PreviousProviderDecision(string Provider, string Model, DateTime CreatedAt, Analysis Decision);
