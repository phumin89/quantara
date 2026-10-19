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
)
{
    public AutonomousTradingStatus? Autonomy { get; init; }

    public string[] ObservedFundingFlows { get; init; } = [];

    public IReadOnlyList<TradingShadow> Shadows { get; init; } = [];

    public TradingEvaluationResult? Evaluation { get; init; }

    public IReadOnlyList<TradingExitStudySample> ExitStudies { get; init; } = [];
}
