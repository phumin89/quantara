using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Portfolio.Checks;

public static class AdaptiveRiskChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.Zero);
        var policy = new RiskPolicy { Profile = RiskProfile.Aggressive };
        var capital = Capital(1000m, 1000m, 0m);
        var market = Market();
        var signal = new RiskSignal(95m, 123, now, "breakout-volume");

        Check(
            AdaptiveRiskEngine
                .Evaluate(
                    policy with
                    {
                        Profile = RiskProfile.Disabled,
                    },
                    capital,
                    market,
                    TradeSide.Buy,
                    100m,
                    signal,
                    now
                )
                .Code == "profileDisabled",
            "adaptive sizing stays disabled until the owner selects a profile"
        );
        var decision = Evaluate(policy, capital, market, signal, now);
        Check(decision.Accepted && decision.Amount is > 0m, "aggressive policy permits a supported tight-stop order");
        Check(decision.Amount is not 500m and not 1000m, "sizing has no legacy 500/1000 hard cap");
        Check(
            decision.Explanation.Contains("not guaranteed", StringComparison.Ordinal),
            "decision discloses stop-gap risk without a max-loss guarantee"
        );
        Check(
            decision == Evaluate(policy, capital, market, signal, now),
            "repeated evidence produces a deterministic decision"
        );

        var doubled = Evaluate(policy, Capital(2000m, 2000m, 0m), market, signal, now);
        var halved = Evaluate(policy, Capital(500m, 500m, 0m), market, signal, now);
        Check(
            doubled.Amount > decision.Amount && halved.Amount < decision.Amount,
            "capacity scales with verified equity and cash"
        );
        Check(doubled.Amount <= 2000m && halved.Amount <= 500m, "capacity remains bounded by available cash");
        Check(
            decimal.Abs(doubled.Amount.GetValueOrDefault() / decision.Amount.GetValueOrDefault() - 2) < .001m
                && decimal.Abs(halved.Amount.GetValueOrDefault() / decision.Amount.GetValueOrDefault() - .5m) < .001m,
            "Capacity scales proportionally apart from fee rounding, without a fixed bankroll."
        );

        Check(
            Evaluate(policy, capital with { SpendableThb = null }, market, signal, now).Code == "unknownCapital",
            "missing capital is unknown rather than zero"
        );
        Check(
            Evaluate(policy, capital with { HasUncertainOrders = true }, market, signal, now).Code == "uncertainOrders",
            "uncertain orders block new sizing"
        );
        Check(
            Evaluate(policy, capital with { ReservedThb = 900m, SpendableThb = 100m }, market, signal, now).Amount
                <= 100m,
            "reserved cash is excluded from spendable capacity"
        );
        Check(
            Evaluate(policy, capital with { ExposureThb = 990m }, market, signal, now).Code
                is "portfolioExposure"
                    or "minimumNotional",
            "global exposure cap is respected"
        );
        Check(
            Evaluate(
                policy,
                capital with
                {
                    Assets = [new CapitalAsset("BTC", 9m, 9m, 100m, 0m)],
                },
                market,
                signal,
                now
            ).Code
                is "concentration"
                    or "minimumNotional",
            "concentration cap is respected"
        );

        Check(
            Evaluate(policy, capital, market with { BuyCapacityThb = 10m }, signal, now)
                is { Accepted: true, Code: "liquidity", Amount: 10m },
            "thin order-book depth reduces capacity"
        );
        Check(
            Evaluate(policy, capital, market with { Ask = 101m }, signal, now).Code == "spread",
            "wide spread is rejected"
        );
        Check(
            Evaluate(policy, capital, market with { Atr = 20m }, signal, now).Code is "riskToStop" or "minimumNotional",
            "wide ATR or stop distance reduces risk capacity"
        );
        Check(
            Evaluate(policy, capital, market with { StopVerified = false }, signal, now).Code == "unsupportedSignal",
            "unverified stop is rejected"
        );
        Check(
            Evaluate(policy, capital, market, signal, now.AddSeconds(91)).Code == "staleEvidence",
            "stale market evidence is rejected"
        );
        Check(
            Evaluate(policy, capital, market, signal with { EvidenceAt = now.AddSeconds(601) }, now).Code
                == "staleEvidence",
            "stale signal evidence is rejected"
        );
        Check(
            Evaluate(policy, capital, market, signal, now.AddSeconds(-6)).Code == "staleEvidence",
            "future evidence beyond clock skew is rejected"
        );

        var sellCapital = Capital(1000m, 1000m, 0m) with { Assets = [new CapitalAsset("BTC", 3m, 2m, 100m, 0.25m)] };
        var sell = AdaptiveRiskEngine.Evaluate(
            policy,
            sellCapital,
            market with
            {
                Bid = 100m,
                Ask = 100.1m,
                SellCapacityQuantity = 99m,
            },
            TradeSide.Sell,
            100m,
            signal,
            now
        );
        Check(sell.Accepted && sell.Amount <= 1.75m, "sell sizing excludes pending base quantity");
        Check(
            AdaptiveRiskEngine.Evaluate(policy, sellCapital, market, TradeSide.Sell, 100m, signal, now).Code
                == "unavailableAssets",
            "sell requires executable bid liquidity"
        );

        Check(
            Evaluate(policy, capital, market with { PriceStep = 0.03m }, signal, now).Code == "unsupportedSignal",
            "unsupported price precision is rejected"
        );
        Check(
            Evaluate(policy, capital, market with { MinimumNotional = 10000m }, signal, now).Code == "minimumNotional",
            "market minimum notional is enforced"
        );
        Check(
            Evaluate(policy, capital, market, signal, now).EstimatedCostThb
                <= Evaluate(policy, capital, market, signal, now).NotionalThb * (policy.EstimatedFeeRate * 2 + .001m)
                    + 0.02m,
            "cost estimate includes spread and rounded round-trip fees"
        );
        Check(
            Evaluate(policy, capital, market with { BuyCapacityThb = 0m }, signal, now).Amount is null,
            "rejected sizing does not expose an amount"
        );

        Console.WriteLine("Adaptive risk checks passed.");
        return Task.CompletedTask;
    }

    private static RiskSizingDecision Evaluate(
        RiskPolicy policy,
        CapitalSnapshot capital,
        RiskMarketEvidence market,
        RiskSignal signal,
        DateTimeOffset now
    ) => AdaptiveRiskEngine.Evaluate(policy, capital, market, TradeSide.Buy, 100m, signal, now);

    private static CapitalSnapshot Capital(decimal cash, decimal equity, decimal exposure) =>
        new(
            "fixture",
            new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.Zero),
            cash,
            equity,
            exposure,
            0m,
            [],
            false,
            null
        );

    private static RiskMarketEvidence Market() =>
        new(
            "BTC",
            new DateTimeOffset(2026, 10, 16, 4, 0, 0, TimeSpan.Zero),
            99.9m,
            100m,
            10000m,
            100m,
            1m,
            1m,
            0.01m,
            2,
            2,
            true,
            true
        );

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
