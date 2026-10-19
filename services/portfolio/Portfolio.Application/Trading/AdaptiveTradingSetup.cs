using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class AdaptiveTradingSetup
{
    public static TradingRuleSet Prepare(TradingRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (rules.Version == "adaptive-intraday-v1")
        {
            return rules with { Version = "adaptive-intraday-v2" };
        }

        if (rules.Adaptive is not null)
        {
            return rules;
        }

        var defaults = new TradingRuleSet();
        return defaults with
        {
            Version = "adaptive-intraday-v2",
            Symbols = [],
            MaxPositions = 16,
            Adaptive = new AdaptiveTradingPolicy
            {
                Risk = new()
                {
                    Profile = RiskProfile.Aggressive,
                    EstimatedFeeRate = defaults.FeeBps / 10000m,
                    MaximumSpreadFraction = defaults.MaxSpreadBps / 10000m,
                    MaximumDataAgeSeconds = defaults.MaxDataAgeSeconds,
                },
            },
        };
    }
}
