namespace Quantara.MarketData.Infrastructure;

public sealed record MarketScanResult(
    IReadOnlyList<MarketScanEntry> Entries,
    int UniverseCount,
    int MissingQuoteCount,
    int ExcludedCount,
    int DuplicateConflictCount
);
