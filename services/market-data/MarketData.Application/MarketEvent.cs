using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public sealed record MarketEvent(
    MarketEventKind Kind,
    string Symbol,
    long Sequence,
    DateTimeOffset ObservedAt,
    long? CandleAt,
    MarketEventBook? Book,
    IReadOnlyList<string> WatchedSymbols,
    bool Dirty,
    long Coalesced
);
