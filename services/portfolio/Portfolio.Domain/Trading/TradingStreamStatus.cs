namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingStreamStatus(
    bool Connected,
    DateTimeOffset? LastEventAt,
    IReadOnlyList<string> WatchedSymbols,
    IReadOnlyList<string> DirtySymbols,
    long Coalesced,
    long Dropped,
    double? DecisionP50Ms,
    double? DecisionP95Ms,
    DateTimeOffset? LastExitEvaluationAt,
    DateTimeOffset? LastReconciliationAt,
    double? LastSubmitLatencyMs
);
