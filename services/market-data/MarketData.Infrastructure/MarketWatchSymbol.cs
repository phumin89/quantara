namespace Quantara.MarketData.Infrastructure;

public sealed record MarketWatchSymbol(string Symbol, int PairingId, decimal QuoteVolume);
