namespace MarketSignalAI.Domain;

public sealed record User(long Id, string DisplayName, string Email);
public sealed record Product(long Id, string Symbol, string Name, string Market, string AssetType, bool IsLeveraged, string QuantityUnit, int UnitSize);
public sealed record AIProvider(long Id, string Code, string DisplayName, int SortOrder);
public sealed record UserPosition(long UserId, long ProductId, decimal Quantity, decimal AverageCost, DateTime UpdatedAt);
public sealed record RootEvent(string Direction, string Status, string Summary);
public sealed record NextAction(string Condition, string Action, decimal? Quantity);
public sealed record Analysis(
    string Action, decimal? Quantity, int Confidence, RootEvent RootEvent,
    string MarketRegime, string Trend, string Momentum, string Volume, string RiskReward,
    string[] Reasons, string[] Risks, string[] BullCase, string[] BearCase,
    string Invalidation, NextAction[] NextActions);
public sealed record AnalysisRecord(long Id, long UserId, long ProductId, long AIProviderId,
    string Model, Analysis Result, string InputSnapshotJson, string RawResponse, DateTime CreatedAt, TokenUsage? Usage = null);
public sealed record TokenUsage(long InputTokens, long OutputTokens, long? CachedTokens = null);
public sealed record ProviderFailure(long UserId, long ProductId, long AIProviderId, string? Model,
    string? ReasoningEffort, string Error, TokenUsage? Usage, DateTime CreatedAt);
public sealed record MarketSnapshot(string Symbol, decimal Price, decimal Open, decimal High, decimal Low,
    decimal PreviousClose, long Volume, DateTimeOffset MarketTime, DateTimeOffset FetchedAt)
{
    public ReferenceQuoteMetadata? QuoteMetadata { get; init; }
}
public sealed record HistoricalPrice(DateOnly TradeDate, decimal? Open, decimal? High, decimal? Low, decimal Close, long? Volume);
public sealed record StoredMarketSnapshot(long Id, long ProductId, MarketSnapshot Snapshot);
public sealed record TradingDay(DateOnly TradeDate, string Market, string Status, DateTimeOffset CheckedAt);
