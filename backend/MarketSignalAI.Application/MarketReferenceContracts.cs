using System.Text.RegularExpressions;
using MarketSignalAI.Domain;

namespace MarketSignalAI.Application;

public interface IMarketReferenceStore
{
    Task<IReadOnlyList<ReferenceInstrument>> GetInstrumentsAsync(long userId, CancellationToken ct);
    Task<ReferenceInstrument> SaveInstrumentAsync(long userId, long? id, string symbol, string name, string market, CancellationToken ct);
    Task DeleteInstrumentAsync(long userId, long id, CancellationToken ct);
    Task<IReadOnlyList<ProductMarketReference>> GetReferencesAsync(long userId, long productId, CancellationToken ct);
    Task<ProductMarketReference> SaveReferenceAsync(long userId, long productId, long? id, long instrumentId, string referenceType, CancellationToken ct);
    Task DeleteReferenceAsync(long userId, long productId, long id, CancellationToken ct);
}

public static class MarketReferenceValidation
{
    public static void Instrument(string symbol, string name, string market)
    {
        YahooSymbol(symbol);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(market) || market.Length > 32)
            throw new ArgumentException("Reference name (1–200 characters) and market (1–32 characters) are required.");
    }
    public static void YahooSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !Regex.IsMatch(symbol, @"\A[A-Z0-9^][A-Z0-9.^=_-]{0,63}\z"))
            throw new ArgumentException("Provide an exact Yahoo ticker of 1–64 uppercase letters/digits or . ^ = _ - characters.");
    }
    public static void Type(string value)
    {
        if (value is not ("UNDERLYING" or "BROAD_MARKET" or "SECTOR"))
            throw new ArgumentException("ReferenceType must be UNDERLYING, BROAD_MARKET or SECTOR.");
    }
}
