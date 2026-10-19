using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed record MarketHistory(
    IReadOnlyList<MarketCandle> Candles,
    int TimeframeSeconds,
    IReadOnlyList<ExecutionHistoryEvidence>? Evidence = null
);
