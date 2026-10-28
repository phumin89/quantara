using System.Security.Cryptography;
using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingPolicyEvaluator(
    ITradingHistoryReader history,
    ITradingMarketReader market,
    TimeProvider time
) : ITradingPolicyEvaluator
{
    public async Task<TradingPolicyEvaluation> Evaluate(
        TradingState state,
        TradingRuleSet proposed,
        CancellationToken cancellationToken
    )
    {
        var now = time.GetUtcNow();
        var baseline = new List<TradingEvaluationMetric>();
        var candidate = new List<TradingEvaluationMetric>();
        var scenarios = new List<RiskSizingSnapshot>();
        var fingerprints = new List<string>();
        if (state.Rules.Adaptive is not { } baselinePolicy || proposed.Adaptive is not { } proposedPolicy)
        {
            return Result(false, "Adaptive policy is required.");
        }

        if (
            state.Autonomy?.Capital
                is not { Blocker: null, HasUncertainOrders: false, EquityThb: >= 10m, SpendableThb: >= 10m } capital
            || capital.ObservedAt > now
            || now - capital.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
        )
        {
            return Result(false, "Waiting for fresh verified account capital.");
        }

        var symbols = state
            .Autonomy.Markets.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(item => item.Symbol)
            .Where(symbol => !baselinePolicy.ExcludedSymbols.Contains(symbol, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        if (symbols.Length == 0)
        {
            return Result(false, "Waiting for observed eligible markets.");
        }

        var books = await market.ReadEvidence(symbols, cancellationToken, adaptiveTimeframes: true);
        foreach (var symbol in symbols)
        {
            var matching = books.Books.Where(book => book.Symbol == symbol).ToArray();
            if (matching.Length != 1 || !TradingReplayEvaluator.HasPrecision(matching[0]))
            {
                return Result(false, "Waiting for unambiguous market precision and native history.");
            }

            var book = matching[0];
            now = time.GetUtcNow();
            if (
                !book.Enabled
                || book.ObservedAt > now
                || now - book.ObservedAt > TimeSpan.FromSeconds(proposed.MaxDataAgeSeconds)
            )
            {
                return Result(false, "Waiting for fresh enabled markets.");
            }

            foreach (var interval in new[] { 300, 900, 3600 })
            {
                var end = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / interval * interval);
                var bars = await history.Read(symbol, interval, end.AddDays(-7), end, cancellationToken);
                if (
                    bars.Count != 7 * 86400 / interval
                    || bars.Min(bar => bar.Timestamp) != end.AddDays(-7).ToUnixTimeSeconds()
                    || bars.Max(bar => bar.Timestamp) != end.ToUnixTimeSeconds() - interval
                )
                {
                    return Result(
                        false,
                        "Waiting for a complete paired seven-day history; missing history remains Unknown."
                    );
                }

                fingerprints.Add(
                    Convert.ToHexString(
                        SHA256.HashData(
                            JsonSerializer.SerializeToUtf8Bytes(
                                new
                                {
                                    book,
                                    interval,
                                    bars,
                                }
                            )
                        )
                    )
                );
                var budget = decimal.Min(capital.SpendableThb.Value, 1000000m);
                baseline.AddRange(TradingReplayEvaluator.Evaluate(state.Rules, symbol, bars, interval, budget, book));
                candidate.AddRange(TradingReplayEvaluator.Evaluate(proposed, symbol, bars, interval, budget, book));
            }

            foreach (var rules in new[] { state.Rules, proposed })
            {
                var scenario = Scenario(rules, capital, book, time.GetUtcNow());
                if (scenario is null || !scenario.Decision.Accepted)
                {
                    return Result(
                        false,
                        "Current capital, spread, liquidity or stop-risk scenario is not executable; keep existing settings."
                    );
                }

                scenarios.Add(scenario);
            }
        }

        if (
            baseline.Count != candidate.Count
            || baseline.Count == 0
            || baseline
                .Concat(candidate)
                .Any(metric => metric.NetThb is null || metric.MaxDrawdownThb is null || metric.UnknownFills > 0)
        )
        {
            return Result(
                false,
                "Paired historical scenarios contain Unknown outcomes; existing settings remain active."
            );
        }

        // Zero-trade strategies do not prove an edge. Require both chronological segments
        // to contain at least one sufficiently sampled, directly comparable strategy.
        foreach (var segment in new[] { "training", "validation" })
        {
            var pairs = baseline.Zip(candidate).Where(pair => pair.First.Segment == segment).ToArray();
            if (
                !pairs.Any(pair => !pair.First.InsufficientSamples && !pair.Second.InsufficientSamples)
                || pairs.Sum(pair => pair.Second.NetThb) < pairs.Sum(pair => pair.First.NetThb)
                || pairs.Sum(pair => pair.Second.NetThb) < 0m
                || pairs.Any(pair => pair.Second.MaxDrawdownThb > capital.EquityThb * proposedPolicy.DailyLossFraction)
            )
            {
                return Result(
                    false,
                    "Candidate lacks sufficient paired evidence or worsens the cost-aware scenario; keep existing settings."
                );
            }
        }

        return Result(
            true,
            "Paired cost-aware scenarios and current account risk checks passed. Historical scenarios are hypothetical, not a profit guarantee."
        );

        TradingPolicyEvaluation Result(bool accepted, string reason) =>
            new(
                accepted,
                reason,
                time.GetUtcNow(),
                Convert
                    .ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fingerprints)))
                    .ToLowerInvariant(),
                baseline,
                candidate,
                scenarios.Count == 0
                    ? null
                    : scenarios.Max(item => item.Decision.NotionalThb + item.Decision.EstimatedCostThb),
                scenarios.Count == 0 ? null : scenarios.Max(item => item.Decision.EstimatedDownsideThb)
            )
            {
                RiskScenarios = scenarios,
                BaselineRulesHash = TradingRuleHash.Compute(state.Rules),
                CandidateRulesHash = TradingRuleHash.Compute(proposed),
            };
    }

    private static RiskSizingSnapshot? Scenario(
        TradingRuleSet rules,
        CapitalSnapshot capital,
        TradingBook book,
        DateTimeOffset now
    )
    {
        var bars = book.Bars.OrderBy(bar => bar.Timestamp).TakeLast(21).ToArray();
        var interval = book.TimeframeSeconds;
        if (
            rules.Adaptive is not { } adaptive
            || bars.Length != 21
            || !TradingTimeframes.Supported(interval)
            || bars[^1].Timestamp != TradingTimeframes.LastCompletedOpen(now, interval)
            || bars.Any(bar =>
                bar.Low <= 0
                || bar.High < decimal.Max(bar.Open, bar.Close)
                || bar.Low > decimal.Min(bar.Open, bar.Close)
                || bar.Volume < 0
            )
            || bars.Zip(bars.Skip(1)).Any(pair => pair.Second.Timestamp - pair.First.Timestamp != interval)
        )
        {
            return null;
        }

        var anchor = bars.TakeLast(20).MinBy(bar => bar.Low);
        if (anchor is null)
        {
            return null;
        }

        var atr = bars.Zip(bars.Skip(1))
            .Average(pair =>
                decimal.Max(
                    pair.Second.High - pair.Second.Low,
                    decimal.Max(
                        decimal.Abs(pair.Second.High - pair.First.Close),
                        decimal.Abs(pair.Second.Low - pair.First.Close)
                    )
                )
            );
        var limit = decimal.Ceiling(book.Ask / book.PriceStep) * book.PriceStep;
        var signal = new RiskSignal(
            anchor.Low,
            anchor.Timestamp,
            book.ObservedAt,
            "ai-policy-current-account-scenario"
        );
        var evidence = new RiskMarketEvidence(
            book.Symbol,
            book.ObservedAt,
            book.Bid,
            book.Ask,
            book.AskQuantity * book.Ask,
            book.BidQuantity,
            atr,
            book.MinimumNotional,
            book.PriceStep,
            book.PriceScale,
            book.QuoteScale,
            book.Enabled,
            true,
            false,
            interval
        );
        var decision = AdaptiveRiskEngine.Evaluate(adaptive.Risk, capital, evidence, TradeSide.Buy, limit, signal, now);
        return new(
            Guid.NewGuid(),
            capital.AccountKey,
            TradingRuleHash.Compute(rules),
            adaptive.Risk,
            capital,
            signal,
            evidence,
            decision,
            0m
        );
    }
}
