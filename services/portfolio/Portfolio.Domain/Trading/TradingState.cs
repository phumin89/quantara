namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingState(
    long Revision,
    TradingMode Mode,
    TradingRuleSet Rules,
    string RulesHash,
    TradingMandate? Mandate,
    bool EmergencyStopped,
    DateTimeOffset? HeartbeatAt,
    string? Fault,
    TradingLedger Paper,
    TradingLedger Live,
    IReadOnlyList<TradingDecisionRecord> Decisions,
    IReadOnlyList<TradingDailyReport> Reports,
    IReadOnlyList<TradingAlert> Alerts
);
