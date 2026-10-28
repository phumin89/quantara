namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingPolicyParameters(
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
)
{
    public TradingRuleSet Apply(TradingRuleSet baseline)
    {
        if (baseline is not { Version: "adaptive-intraday-v2", Adaptive: { } adaptive })
        {
            throw new ArgumentException("AI-managed settings require adaptive-intraday-v2.");
        }

        var next = baseline with
        {
            MaxPositions = this.MaxPositions,
            MaxSpreadBps = this.MaxSpreadBps,
            SlippageBps = this.SlippageBps,
            TakeProfitBps = this.TakeProfitBps,
            StopLossBps = this.StopLossBps,
            TrailingStopBps = this.TrailingStopBps,
            VolumeRatio = this.VolumeRatio,
            TimeExitMinutes = this.TimeExitMinutes,
            CooldownMinutes = this.CooldownMinutes,
            OrderLifetimeMinutes = this.OrderLifetimeMinutes,
            Adaptive = adaptive with
            {
                DailyLossFraction = this.DailyLossFraction,
                Risk = adaptive.Risk with
                {
                    MaximumExposureFraction = this.MaximumExposureFraction,
                    MaximumConcentrationFraction = this.MaximumConcentrationFraction,
                    RiskToStopFraction = this.RiskToStopFraction,
                    MaximumSpreadFraction = this.MaxSpreadBps / 10000m,
                    AtrMultiplier = this.AtrMultiplier,
                },
            },
        };
        next.Validate();
        return next;
    }
}
