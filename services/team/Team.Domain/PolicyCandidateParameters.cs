namespace Quantara.Team.Domain;

public sealed record PolicyCandidateParameters(
    int MaxPositions,
    decimal MaxSpreadBps,
    decimal SlippageBps,
    decimal TakeProfitBps,
    decimal StopLossBps,
    decimal TrailingStopBps,
    decimal VolumeRatio,
    int TimeExitMinutes,
    int CooldownMinutes,
    int OrderLifetimeMinutes,
    decimal MaximumExposureFraction,
    decimal MaximumConcentrationFraction,
    decimal RiskToStopFraction,
    decimal DailyLossFraction,
    decimal AtrMultiplier
);
