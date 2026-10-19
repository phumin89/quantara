using System.Globalization;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingEvaluationService(ITradingHistoryReader history, TimeProvider time)
{
    public async Task<TradingEvaluationResult> Evaluate(
        TradingEvaluationRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            request.Symbols.Count is < 1 or > 4
            || request.Symbols.Distinct(StringComparer.Ordinal).Count() != request.Symbols.Count
            || request.Symbols.Any(symbol =>
                string.IsNullOrEmpty(symbol)
                || symbol.Length > 30
                || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
            || request.TimeframeSeconds != 0 && !TradingTimeframes.Supported(request.TimeframeSeconds)
            || request.Rules is not { Version: "adaptive-intraday-v2" } rules
        )
        {
            throw new ArgumentException("Evaluation requires one to four unique symbols and v2 rules.");
        }

        rules.Validate();
        var observedAt = time.GetUtcNow();
        var metrics = new List<TradingEvaluationMetric>();
        var warnings = new List<string>
        {
            FormattableString.Invariant(
                $"Hypothetical OHLC scenario, not account profit or broker fills. Reference budget THB {request.ReferenceBudgetThb}; fees {rules.FeeBps} bps per side, assumed spread {rules.MaxSpreadBps} bps and exit slippage {rules.SlippageBps} bps. Historical quote/queue data is unavailable."
            ),
            "Chronological 70/30 training/validation; positions reset at the boundary. No optimization or automatic promotion. Later-bar strict trade-through is required; touch, partial-window expiry, missing bars, ambiguous exits and unclosed positions are Unknown. At least 20 closed trades and 30 evaluated bars per segment are required for sufficient samples.",
        };
        foreach (var symbol in request.Symbols)
        {
            foreach (
                var interval in request.TimeframeSeconds == 0
                    ? TradingEvaluationService.intervals
                    : [request.TimeframeSeconds]
            )
            {
                var end = DateTimeOffset.FromUnixTimeSeconds(observedAt.ToUnixTimeSeconds() / interval * interval);
                IReadOnlyList<TradingBar> bars;
                try
                {
                    bars = await history.Read(symbol, interval, end.AddDays(-7), end, cancellationToken);
                }
                catch (Exception exception)
                    when (exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException
                        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested
                    )
                {
                    bars = [];
                    warnings.Add(
                        symbol
                            + " "
                            + (interval / 60).ToString(CultureInfo.InvariantCulture)
                            + "m: historical data unavailable; results Unknown."
                    );
                }

                if (bars.Count != 7 * 86400 / interval)
                {
                    warnings.Add(
                        symbol
                            + " "
                            + (interval / 60).ToString(CultureInfo.InvariantCulture)
                            + "m: incomplete seven-day window; missing data is not evidence of no trades."
                    );
                }

                var evaluated = TradingReplayEvaluator.Evaluate(
                    rules,
                    symbol,
                    bars,
                    interval,
                    request.ReferenceBudgetThb
                );
                metrics.AddRange(
                    bars.Count == 7 * 86400 / interval
                        ? evaluated
                        : evaluated.Select(metric =>
                            metric with
                            {
                                NetThb = null,
                                MaxDrawdownThb = null,
                                InsufficientSamples = true,
                            }
                        )
                );
            }
        }

        return new("ohlc-scenario-v1", false, metrics, warnings, observedAt, TradingRuleHash.Compute(rules));
    }

    private static readonly int[] intervals = [300, 900, 3600];
}
