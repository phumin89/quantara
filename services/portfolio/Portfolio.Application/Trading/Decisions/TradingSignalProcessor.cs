using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingSignalProcessor(
    ITradingStore store,
    IAccountContext account,
    AutonomousRiskService? adaptiveRisk
)
{
    private readonly TradingStateWriter writer = new(store);

    internal async Task<(TradingState State, bool ExitEvaluated)> Process(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        AutonomousTradingBatch? scan,
        TradingMarketEvent? trigger,
        bool stale,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var live = state.Mode == TradingMode.AutoLive;
        var adaptive = state.Rules.Adaptive;
        var entries = trigger is null || trigger.Kind == TradingEventKind.CandleClosed;
        var ranked = adaptive is null ? state.Rules.Symbols : TradingDecisionEvaluation.RankMarkets(state, books, now);
        var exitEvaluated = false;
        foreach (var symbol in ranked)
        {
            var book = books.SingleOrDefault(item => item.Symbol == symbol);
            var ledger = live ? state.Live : state.Paper;
            var position = ledger.Positions.SingleOrDefault(item => item.Symbol == symbol && item.Quantity > 0);
            if (!entries && position is null)
            {
                continue;
            }

            if (book is null || !TradingExecutionRules.Fresh(book, state.Rules, now, position is null))
            {
                continue;
            }

            exitEvaluated |= position is not null;

            if (ledger.Orders.Any(order => order.Symbol == symbol && TradingLedgerAccounting.Active(order)))
            {
                continue;
            }

            var decision = TradingDecisionEvaluation.Evaluate(state.Rules, book, position, now);
            var signalAction = decision.Action;
            if (
                decision.Action == TradingAction.Buy
                && trigger is not null
                && (
                    state.Rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3"
                        ? decision.Candle + decision.TimeframeSeconds > trigger.CandleAt + 300
                        : trigger.CandleAt != decision.Candle
                )
            )
            {
                continue;
            }

            if (
                adaptive is not null
                && decision.Action == TradingAction.Buy
                && decision.StopPrice is not null
                && scan is not null
                && scan.EntrySymbols.Contains(symbol, StringComparer.Ordinal)
            )
            {
                var sampleKey =
                    state.RulesHash
                    + ":"
                    + symbol
                    + ":"
                    + decision.Candle.ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + decision.StrategyVersion;
                if (
                    !state.Shadows.Any(sample => sample.Key == sampleKey)
                    && state.Shadows.Count(sample =>
                        sample.State is TradingShadowState.Waiting or TradingShadowState.Open
                    ) < 8
                )
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Shadows = state
                                .Shadows.Append(
                                    TradingShadowProtocol.Capture(sampleKey, state.Rules, book, decision, now)
                                )
                                .ToArray(),
                        },
                        cancellationToken
                    );
                }
            }

            if (decision.Action == TradingAction.Sell)
            {
                decision = decision with { Candle = now.ToUnixTimeSeconds() / 300 * 300 - 300 };
            }

            if (decision.Action == TradingAction.Buy)
            {
                if (scan is not null && !scan.EntrySymbols.Contains(symbol, StringComparer.Ordinal))
                {
                    decision = decision with
                    {
                        Action = TradingAction.Wait,
                        Reason = "Owned market monitored for exits only; outside the entry universe.",
                    };
                }

                var equity = TradingLedgerAccounting.Equity(ledger, books, now, state.Rules.MaxDataAgeSeconds);
                var loss = TradingLedgerValuation.DailyResult(ledger, equity, now);
                var slots = ledger
                    .Positions.Where(item => item.Quantity > 0)
                    .Select(item => item.Symbol)
                    .Concat(ledger.Orders.Where(TradingLedgerAccounting.Active).Select(item => item.Symbol))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var previous = ledger
                    .Orders.Where(order => order.Symbol == symbol)
                    .OrderBy(order => order.CreatedAt)
                    .LastOrDefault();
                if (loss is null || loss <= -TradingLedgerValuation.LossCap(state))
                {
                    decision = decision with
                    {
                        Action = TradingAction.Wait,
                        Reason = "Daily loss or unknown valuation",
                    };
                }
                else if (
                    stale
                    || slots >= state.Rules.MaxPositions
                    || adaptive is null
                        && ledger.Positions.Sum(item => item.CostThb)
                            + TradingLedgerAccounting.ReservedCash(ledger)
                            + state.Rules.PerOrderThb
                            > state.Rules.AllocatedThb
                    || adaptive is null
                        && ledger.CashThb - TradingLedgerAccounting.ReservedCash(ledger) < state.Rules.PerOrderThb
                    || previous is not null
                        && now - (previous.LastInspectedAt ?? previous.CreatedAt)
                            < TimeSpan.FromMinutes(state.Rules.CooldownMinutes)
                )
                {
                    decision = decision with
                    {
                        Action = TradingAction.Wait,
                        Reason = "Capacity, available cash, cooldown or feed guard",
                    };
                }
            }

            if (state.Autonomy is { } status)
            {
                state = await this.writer.Save(
                    state,
                    state with
                    {
                        Autonomy = status with
                        {
                            Markets = status
                                .Markets.Where(item => item.Symbol != symbol)
                                .Append(
                                    new TradingScanObservation(
                                        symbol,
                                        now,
                                        decision.Reason,
                                        decision.StrategyVersion,
                                        AdaptiveTradingStrategy.OpportunityScore(state.Rules, book, decision)
                                    )
                                )
                                .ToArray(),
                        },
                    },
                    cancellationToken
                );
            }

            var key = TradingDecisionEvaluation.Key(state, symbol, decision);
            if (state.Decisions.Any(item => item.Key == key))
            {
                continue;
            }

            var record = new TradingDecisionRecord(
                key,
                symbol,
                decision.Candle,
                decision.Action,
                decision.Reason,
                now,
                state.Mode,
                decision.TimeframeSeconds,
                state.Rules.Version is "adaptive-intraday-v2" or "adaptive-intraday-v3" ? signalAction : null
            );
            var next = state with { Decisions = state.Decisions.Append(record).ToArray() };
            if (decision.Action != TradingAction.Wait)
            {
                var side = decision.Action == TradingAction.Buy ? TradeSide.Buy : TradeSide.Sell;
                var limit = TradingExecutionRules.Floor(
                    (
                        side == TradeSide.Buy && decision.EntryLimit is { } restingLimit ? restingLimit
                        : side == TradeSide.Buy ? book.Ask * (1 + state.Rules.SlippageBps / 10000m)
                        : book.Bid * (1 - state.Rules.SlippageBps / 10000m)
                    ),
                    book.PriceStep
                );
                var intentId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
                RiskSizingSnapshot? sizing = null;
                if (adaptive is not null && side == TradeSide.Buy)
                {
                    sizing = await (
                        adaptiveRisk ?? throw new InvalidOperationException("Autonomous risk service is unavailable.")
                    ).Size(
                        intentId,
                        state.Rules,
                        book,
                        limit,
                        new RiskSignal(
                            decision.StopPrice,
                            decision.StopCandle,
                            DateTimeOffset.FromUnixTimeSeconds(decision.Candle + decision.TimeframeSeconds),
                            decision.StrategyVersion ?? decision.Reason
                        ),
                        decision.EntryLimit is not null,
                        cancellationToken,
                        live
                            ? null
                            : PaperCapitalSnapshot.Create(account.AccountKey, state.Paper, books, state.Rules, now)
                    );
                    if (!sizing.Decision.Accepted)
                    {
                        next = next with
                        {
                            Autonomy = next.Autonomy is { } rejectedStatus
                                ? rejectedStatus with
                                {
                                    Markets = rejectedStatus
                                        .Markets.Select(item =>
                                            item.Symbol == symbol
                                                ? item with
                                                {
                                                    Reason = sizing.Decision.Explanation,
                                                }
                                                : item
                                        )
                                        .ToArray(),
                                }
                                : null,
                            Decisions = state
                                .Decisions.Append(
                                    record with
                                    {
                                        Action = TradingAction.Wait,
                                        Reason = sizing.Decision.Explanation,
                                    }
                                )
                                .ToArray(),
                        };
                        state = await this.writer.Save(state, next, cancellationToken);
                        continue;
                    }
                }

                var amount =
                    side == TradeSide.Buy
                        ? TradingExecutionRules.Floor(
                            sizing?.Decision.Amount ?? state.Rules.PerOrderThb,
                            TradingExecutionRules.Scale(book.QuoteScale)
                        )
                        : TradingExecutionRules.Floor(
                            Math.Min(
                                (
                                    position ?? throw new InvalidDataException("Exit requires an owned position.")
                                ).Quantity,
                                adaptive is not null ? position.Quantity : state.Rules.PerOrderThb / limit
                            ),
                            TradingExecutionRules.Scale(book.BaseScale)
                        );
                var market = new OrderMarket(
                    book.Bid,
                    book.MinimumNotional,
                    book.PriceStep,
                    book.PriceScale,
                    side == TradeSide.Buy ? book.QuoteScale : book.BaseScale,
                    side == TradeSide.Sell && adaptive is not null ? book.SellEnabled ?? book.Enabled : book.Enabled
                );
                if (
                    side == TradeSide.Sell
                    && adaptive is null
                    && position is not null
                    && position.Quantity * limit > state.Rules.PerOrderThb
                    && (position.Quantity - amount) * limit < book.MinimumNotional
                )
                {
                    amount = TradingExecutionRules.Floor(
                        position.Quantity - book.MinimumNotional / limit,
                        TradingExecutionRules.Scale(book.BaseScale)
                    );
                }

                var intent = new TradingIntent(
                    intentId,
                    symbol,
                    side,
                    amount,
                    limit,
                    amount,
                    now,
                    decision.Candle,
                    state.RulesHash,
                    decision.Reason,
                    "pending",
                    Sizing: sizing
                )
                {
                    ExitPlan =
                        side == TradeSide.Buy && state.Rules.ExitPolicy is not null
                            ? AdaptiveExitStrategy.Create(state.Rules, book, decision, intentId, state.RulesHash, now)
                        : side == TradeSide.Sell ? position?.ExitPlan
                        : null,
                    ExitStudyPlan =
                        side == TradeSide.Buy && state.Rules.Version == "adaptive-intraday-v2"
                            ? TradingDecisionEvaluation.StudySeed(state.Rules, book, decision, intentId, now)
                            : null,
                };
                try
                {
                    OrderRules.Validate(TradingExecutionRules.Proposal(intent), market);
                    var updated = ledger with { Orders = ledger.Orders.Append(intent).ToArray() };
                    next = live ? next with { Live = updated } : next with { Paper = updated };
                }
                catch (OrderValidationException)
                {
                    next = next with
                    {
                        Decisions = state
                            .Decisions.Append(
                                record with
                                {
                                    Action = TradingAction.Wait,
                                    Reason = "Market minimum or precision guard",
                                }
                            )
                            .ToArray(),
                    };
                }
            }

            state = await this.writer.Save(state, next, cancellationToken);
        }

        return (state, exitEvaluated);
    }
}
