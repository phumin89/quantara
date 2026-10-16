using System.Globalization;

namespace Quantara.Portfolio.Domain.Risk;

public static class AdaptiveRiskEngine
{
    public static RiskSizingDecision Evaluate(
        RiskPolicy policy,
        CapitalSnapshot capital,
        RiskMarketEvidence market,
        TradeSide side,
        decimal limit,
        RiskSignal signal,
        DateTimeOffset now
    )
    {
        policy.Validate();
        if (policy.Profile != RiskProfile.Aggressive)
        {
            return Reject("profileDisabled", "Choose a risk profile before adaptive sizing.");
        }

        if (
            capital.Blocker is not null
            || capital.SpendableThb is null
            || capital.EquityThb is null
            || capital.ExposureThb is null
            || capital.ReservedThb is null
        )
        {
            return Reject("unknownCapital", capital.Blocker ?? "Capital, prices or available balances are incomplete.");
        }

        if (capital.HasUncertainOrders)
        {
            return Reject("uncertainOrders", "Reconcile uncertain orders before calculating another size.");
        }

        if (
            !Fresh(capital.ObservedAt, now, policy.MaximumDataAgeSeconds)
            || !Fresh(market.ObservedAt, now, policy.MaximumDataAgeSeconds)
            || !Fresh(signal.EvidenceAt, now, policy.MaximumSignalAgeSeconds)
        )
        {
            return Reject("staleEvidence", "Capital, quote or strategy evidence is stale. Request fresh evidence.");
        }

        if (
            !Enum.IsDefined(side)
            || !market.Enabled
            || market.Bid <= 0
            || market.Ask < market.Bid
            || market.Atr <= 0
            || market.MinimumNotional <= 0
            || market.PriceStep <= 0
            || market.AmountScale is < 0 or > 18
            || market.PriceScale is < 0 or > 18
            || limit <= 0
            || decimal.Round(limit, market.PriceScale) != limit
            || limit % market.PriceStep != 0
            || string.IsNullOrWhiteSpace(signal.StrategyEvidence)
            || signal.StrategyEvidence.Length > 160
            || (
                side == TradeSide.Buy
                && (
                    !market.StopVerified
                    || signal.StopPrice is null or <= 0
                    || signal.StopCandle is null or <= 0
                    || signal.StopPrice >= limit
                )
            )
        )
        {
            return Reject(
                "unsupportedSignal",
                "A verified completed-candle stop, ATR, market rules and exact limit are required."
            );
        }

        if ((market.Ask - market.Bid) / ((market.Ask + market.Bid) / 2) > policy.MaximumSpreadFraction)
        {
            return Reject("spread", "The bid/ask spread exceeds the selected policy.");
        }

        if (
            capital.EquityThb <= 0
            || capital.SpendableThb < 0
            || capital.ExposureThb < 0
            || capital.ReservedThb < 0
            || capital.Assets.Select(asset => asset.Symbol).Distinct(StringComparer.Ordinal).Count()
                != capital.Assets.Count
        )
        {
            return Reject("invalidCapital", "Positive verified equity and consistent account balances are required.");
        }

        var equity = capital.EquityThb.Value;
        var exposure = capital.ExposureThb.Value;
        var asset = capital.Assets.SingleOrDefault(item => item.Symbol == market.Symbol);
        decimal amount;
        decimal notional;
        decimal downside;
        decimal cost;
        var code = "availableAssets";
        if (side == TradeSide.Sell)
        {
            if (asset?.Available is null || asset.Available < 0 || asset.PendingSellQuantity < 0 || market.Bid < limit)
            {
                return Reject("unavailableAssets", "Sell quantity or executable bid liquidity is unavailable.");
            }

            amount = Floor(
                decimal.Min(
                    decimal.Max(0, asset.Available.Value - asset.PendingSellQuantity),
                    market.SellCapacityQuantity
                ),
                market.AmountScale
            );
            notional = amount * limit;
            cost = Fee(notional, policy.EstimatedFeeRate);
            downside = cost;
        }
        else
        {
            if (
                market.Ask > limit
                || market.BuyCapacityThb <= 0
                || asset is not null && (asset.PriceThb is null || asset.Quantity < 0 || asset.CommittedBuyThb < 0)
            )
            {
                return Reject(
                    "liquidity",
                    "No verified executable ask liquidity at this limit. A resting order needs a fresh executable sizing proposal."
                );
            }

            if (signal.StopPrice is not { } stop)
            {
                return Reject("unsupportedSignal", "A verified stop price is required.");
            }

            var distance = decimal.Max(limit - stop, market.Atr * policy.AtrMultiplier) / limit;
            var entryMarkLoss = decimal.Max(0, 1 - market.Bid / limit);
            var assetExposure = (asset?.Quantity ?? 0) * (asset?.PriceThb ?? 0) + (asset?.CommittedBuyThb ?? 0);
            // THB fees are rounded up to two decimals; reserve both rounding increments.
            var capacities = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["availableCash"] = (capital.SpendableThb.Value - 0.01m) / (1 + policy.EstimatedFeeRate),
                ["portfolioExposure"] =
                    (equity * policy.MaximumExposureFraction - exposure - 0.01m)
                    / (1 + policy.MaximumExposureFraction * (policy.EstimatedFeeRate + entryMarkLoss)),
                ["concentration"] =
                    (equity * policy.MaximumConcentrationFraction - assetExposure - 0.01m)
                    / (1 + policy.MaximumConcentrationFraction * (policy.EstimatedFeeRate + entryMarkLoss)),
                ["riskToStop"] =
                    (equity * policy.RiskToStopFraction - 0.02m) / (distance + 2 * policy.EstimatedFeeRate),
                ["liquidity"] = market.BuyCapacityThb,
            };
            var binding = capacities.MinBy(item => item.Value);
            code = binding.Key;
            amount = Floor(decimal.Max(0, binding.Value), market.AmountScale);
            notional = amount;
            var roundTripFees = 2 * Fee(notional, policy.EstimatedFeeRate);
            cost = roundTripFees + notional * entryMarkLoss;
            downside = notional * distance + roundTripFees;
        }

        if (amount <= 0 || notional < market.MinimumNotional)
        {
            return Reject(
                "minimumNotional",
                "Capacity after liquidity, fees and policy limits is below the market minimum."
            );
        }

        var projected = side == TradeSide.Buy ? exposure + notional : decimal.Max(0, exposure - notional);
        var explanation = string.Create(
            CultureInfo.InvariantCulture,
            $"Size limited by {code}; {notional / equity:P2} of verified equity. Estimated costs {cost:0.00} THB (fees and entry mark spread); stop/ATR scenario {downside:0.00} THB. Stops and LIMIT fills are not guaranteed; gaps may lose the entire position. No strategy win rate was assumed."
        );
        return new(true, code, amount, notional, notional / equity, projected, downside, cost, explanation);
    }

    private static RiskSizingDecision Reject(string code, string reason) =>
        new(false, code, null, null, null, null, null, null, reason);

    private static bool Fresh(DateTimeOffset observed, DateTimeOffset now, int seconds) =>
        observed <= now.AddSeconds(5) && now - observed <= TimeSpan.FromSeconds(seconds);

    private static decimal Fee(decimal notional, decimal rate) => decimal.Ceiling(notional * rate * 100m) / 100m;

    private static decimal Floor(decimal value, int scale)
    {
        var multiplier = (decimal)Math.Pow(10, scale);
        return decimal.Floor(value * multiplier) / multiplier;
    }
}
