namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskSignal(
    decimal? StopPrice,
    long? StopCandle,
    DateTimeOffset EvidenceAt,
    string StrategyEvidence
);
