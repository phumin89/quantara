using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class TradingStateFactory
{
    public static TradingState Create(DateTimeOffset now)
    {
        var rules = new TradingRuleSet();
        var paper = new TradingLedger(
            rules.AllocatedThb,
            rules.AllocatedThb,
            now,
            [],
            [],
            [],
            [new TradingEquityMark(now, rules.AllocatedThb, rules.AllocatedThb)]
        );
        var live = new TradingLedger(0, 0, now, [], [], [], []);
        return new TradingState(
            0,
            TradingMode.Paper,
            rules,
            TradingRuleHash.Compute(rules),
            null,
            false,
            null,
            null,
            paper,
            live,
            [],
            [],
            []
        );
    }
}
