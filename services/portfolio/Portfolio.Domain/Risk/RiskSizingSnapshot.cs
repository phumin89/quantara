namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskSizingSnapshot(
    Guid Id,
    string AccountKey,
    string PolicyHash,
    RiskPolicy Policy,
    CapitalSnapshot Capital,
    RiskSignal Signal,
    RiskMarketEvidence Market,
    RiskSizingDecision Decision,
    decimal RequestedAmount
);
