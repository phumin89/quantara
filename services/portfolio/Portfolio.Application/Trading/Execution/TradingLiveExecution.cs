using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingLiveExecution(
    ITradingStore store,
    ITradingExchange exchange,
    IAccountContext account,
    TimeProvider time,
    AutonomousRiskService? adaptiveRisk,
    TradingEventDispatcher? events
)
{
    private readonly TradingStateWriter writer = new(store);

    internal async Task<TradingState> Execute(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        bool entries,
        CancellationToken cancellationToken
    )
    {
        foreach (var original in state.Live.Orders.Where(TradingLedgerAccounting.Active).ToArray())
        {
            if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
            {
                return state;
            }

            var book = books.SingleOrDefault(item => item.Symbol == original.Symbol);
            if (
                original.Status == "pending"
                && time.GetUtcNow() - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
            )
            {
                state = await this.writer.Save(
                    state,
                    state with
                    {
                        Live = TradingLedgerAccounting.ReplaceOrder(state.Live, original with { Status = "cancelled" }),
                    },
                    cancellationToken
                );
                continue;
            }

            if (original.Status == "pending" && original.Side == TradeSide.Buy && !entries)
            {
                continue;
            }

            if (original.Status != "pending")
            {
                var holding = state.Live.Positions.SingleOrDefault(position =>
                    position.Symbol == original.Symbol && position.Quantity > 0
                );
                var exitNeeded =
                    original.Side == TradeSide.Buy
                    && book is not null
                    && (
                        (
                            holding is not null
                            && TradingDecisionEvaluation.Evaluate(state.Rules, book, holding, now).Action
                                == TradingAction.Sell
                        )
                        || original.Sizing?.Signal.StopPrice is { } stop && book.Bid <= stop
                    );
                if (
                    original.Status is "submitted" or "partial"
                    && original.CancellationState is null
                    && (
                        exitNeeded || now - original.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes)
                    )
                )
                {
                    using var prepared = await exchange.PrepareCancellation(original, cancellationToken);
                    var claimed = original with { CancellationState = "claimed" };
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(state.Live, claimed),
                        },
                        cancellationToken
                    );
                    var cancellationAuthority = await store.Read(cancellationToken);
                    if (
                        !TradingAuthority.Valid(cancellationAuthority, account.AccountKey, time.GetUtcNow())
                        || cancellationAuthority.Mandate?.Id != state.Mandate?.Id
                        || cancellationAuthority.Live.Orders.Single(order => order.Id == original.Id).CancellationState
                            != "claimed"
                    )
                    {
                        return cancellationAuthority;
                    }

                    state = cancellationAuthority;
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                        var result = await prepared.Send(timeout.Token);
                        claimed = claimed with { CancellationState = result.Accepted ? "acknowledged" : "unknown" };
                    }
                    catch (Exception)
                    {
                        claimed = claimed with { CancellationState = "unknown" };
                    }

                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(state.Live, claimed),
                        },
                        CancellationToken.None
                    );
                }

                continue;
            }

            if (
                book is null
                || !TradingExecutionRules.Fresh(book, state.Rules, now, original.Side != TradeSide.Sell)
                || original.RulesHash != state.RulesHash
                || state.Live.Orders.Any(order =>
                    order.Status is "submitting" or "unknown" || order.CancellationState is "claimed" or "unknown"
                )
            )
            {
                return state;
            }

            var market = await exchange.Market(original.Symbol, original.Side, cancellationToken);
            OrderRules.Validate(TradingExecutionRules.Proposal(original), market);
            var balances = await exchange.Balances(cancellationToken);
            var quote = events?.LatestBook(original.Symbol);
            if (events is not null && quote is null)
            {
                return state;
            }

            book = quote is null ? book : quote with { Bars = book.Bars, TimeframeSeconds = book.TimeframeSeconds };
            if (!TradingExecutionRules.Fresh(book, state.Rules, time.GetUtcNow(), original.Side != TradeSide.Sell))
            {
                return state;
            }

            if (
                original.Side == TradeSide.Buy
                && TradingLedgerValuation.DailyResult(
                    state.Live,
                    TradingLedgerAccounting.Equity(state.Live, books, time.GetUtcNow(), state.Rules.MaxDataAgeSeconds),
                    time.GetUtcNow()
                )
                    is null
            )
            {
                return state;
            }

            if (
                original.Side == TradeSide.Buy
                && TradingLedgerValuation.DailyResult(
                    state.Live,
                    TradingLedgerAccounting.Equity(state.Live, books, time.GetUtcNow(), state.Rules.MaxDataAgeSeconds),
                    time.GetUtcNow()
                ) <= -TradingLedgerValuation.LossCap(state)
            )
            {
                state = await this.writer.Save(
                    state,
                    state with
                    {
                        Live = TradingLedgerAccounting.ReplaceOrder(state.Live, original with { Status = "cancelled" }),
                    },
                    cancellationToken
                );
                continue;
            }

            var currency = original.Side == TradeSide.Buy ? "THB" : original.Symbol;
            if (original.Side == TradeSide.Buy && state.Rules.Adaptive is not null)
            {
                var retained = original.Sizing;
                var current =
                    retained is not null
                    && retained.PolicyHash == state.RulesHash
                    && retained.AccountKey == account.AccountKey
                        ? await (
                            adaptiveRisk
                            ?? throw new InvalidOperationException("Autonomous risk service is unavailable.")
                        ).Size(
                            original.Id,
                            state.Rules,
                            book,
                            original.LimitPrice,
                            retained.Signal,
                            retained.Market.Resting,
                            cancellationToken
                        )
                        : null;
                if (current is null || !current.Decision.Accepted || current.Decision.Amount < original.Amount)
                {
                    state = await this.writer.Save(
                        state,
                        state with
                        {
                            Live = TradingLedgerAccounting.ReplaceOrder(
                                state.Live,
                                original with
                                {
                                    Status = "cancelled",
                                }
                            ),
                        },
                        cancellationToken
                    );
                    continue;
                }
            }

            var available = balances.SingleOrDefault(balance => balance.Symbol == currency)?.Available;
            var reserved = state
                .Live.Orders.Where(order =>
                    order.Id != original.Id && TradingLedgerAccounting.Active(order) && order.Side == TradeSide.Buy
                )
                .Sum(order => order.Remaining);
            if (
                available is null
                || available < original.Amount
                || original.Side == TradeSide.Buy && state.Live.CashThb - reserved < original.Amount
                || original.Side == TradeSide.Buy
                    && state.Rules.Adaptive is null
                    && state.Live.Positions.Sum(position => position.CostThb) + reserved + original.Amount
                        > state.Rules.AllocatedThb
                || !await this.PositionsReconciled(state.Live, balances, original, market, cancellationToken)
            )
            {
                throw new InvalidDataException("Available funds or strategy position reconciliation mismatch.");
            }

            using var submission = await exchange.Prepare(original, cancellationToken);
            var intent = original with { Status = "submitting" };
            if (!TradingAuthority.Valid(state, account.AccountKey, time.GetUtcNow()))
            {
                return state;
            }

            // This durable CAS is the only entry into the autonomous broker POST path.
            state = await this.writer.Save(
                state,
                state with
                {
                    Live = TradingLedgerAccounting.ReplaceOrder(state.Live, intent),
                },
                cancellationToken
            );
            events?.RecordSubmission(
                TimeSpan.FromMilliseconds(Math.Max(0, (time.GetUtcNow() - original.CreatedAt).TotalMilliseconds))
            );
            var submissionAuthority = await store.Read(cancellationToken);
            if (
                !TradingAuthority.Valid(submissionAuthority, account.AccountKey, time.GetUtcNow())
                || submissionAuthority.Mandate?.Id != state.Mandate?.Id
                || submissionAuthority.Live.Orders.Single(order => order.Id == intent.Id).Status != "submitting"
                || events is not null
                    && (
                        events.LatestBook(original.Symbol) is not { } finalQuote
                        || !TradingExecutionRules.Fresh(
                            finalQuote,
                            state.Rules,
                            time.GetUtcNow(),
                            original.Side != TradeSide.Sell
                        )
                    )
            )
            {
                return submissionAuthority;
            }

            state = submissionAuthority;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await submission.Send(timeout.Token);
                intent = intent with
                {
                    Status = result.Accepted ? "submitted" : "rejected",
                    BrokerOrderId = result.OrderId,
                    LastInspectedAt = now,
                };
            }
            catch (Exception)
            {
                intent = intent with { Status = "unknown", LastInspectedAt = now };
            }

            adaptiveRisk?.Invalidate();
            var next = state with { Live = TradingLedgerAccounting.ReplaceOrder(state.Live, intent) };
            if (intent.Status == "unknown")
            {
                next = TradingAuthority.Fault(next, "Uncertain broker order — reconciliation required", now);
            }

            state = await this.writer.Save(state, next, CancellationToken.None);
        }

        return state;
    }

    private async Task<bool> PositionsReconciled(
        TradingLedger ledger,
        IReadOnlyList<Balance> balances,
        TradingIntent current,
        OrderMarket currentMarket,
        CancellationToken cancellationToken
    )
    {
        foreach (var position in ledger.Positions.Where(position => position.Quantity > 0))
        {
            var quantity = balances.SingleOrDefault(balance => balance.Symbol == position.Symbol)?.Quantity;
            if (
                !await TradingPositionBalance.Covers(
                    quantity,
                    position,
                    exchange,
                    cancellationToken,
                    current.Symbol == position.Symbol && current.Side == TradeSide.Sell
                        ? currentMarket.AmountScale
                        : null
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
