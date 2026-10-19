namespace Quantara.MarketData.Domain;

public sealed record ExecutionHistoryEvidence(
    string Symbol,
    DateTimeOffset CheckedAt,
    string Source,
    int TimeframeSeconds,
    int CompletedBars,
    int ContiguousBars,
    int MissingIntervals,
    string Status,
    string GapCause,
    string? Reason,
    int VerifiedZeroVolumeBars = 0
);
