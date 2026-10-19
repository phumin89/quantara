using System.Globalization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingPaperExecution(ITradingStore store)
{
    private readonly TradingStateWriter writer = new(store);

    internal async Task<TradingState> Fill(
        TradingState state,
        IReadOnlyList<TradingBook> books,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var ledger = state.Paper;
        foreach (var order in ledger.Orders.Where(order => order.Status is "pending" or "partial").ToArray())
        {
            if (now - order.CreatedAt >= TimeSpan.FromMinutes(state.Rules.OrderLifetimeMinutes))
            {
                ledger = TradingLedgerAccounting.ReplaceOrder(
                    ledger,
                    order with
                    {
                        Status = "cancelled",
                        LastInspectedAt = now,
                    }
                );
                continue;
            }

            var book = books.SingleOrDefault(book => book.Symbol == order.Symbol);
            if (
                book is null
                || !TradingExecutionRules.Fresh(book, state.Rules, now, order.Side != TradeSide.Sell)
                || book.ObservedAt <= order.LastFillObservation
            )
            {
                continue;
            }

            var holding = ledger.Positions.SingleOrDefault(position =>
                position.Symbol == order.Symbol && position.Quantity > 0
            );
            if (
                order.Side == TradeSide.Buy
                && holding is not null
                && TradingDecisionEvaluation.Evaluate(state.Rules, book, holding, now).Action == TradingAction.Sell
            )
            {
                ledger = TradingLedgerAccounting.ReplaceOrder(
                    ledger,
                    order with
                    {
                        Status = "cancelled",
                        LastInspectedAt = now,
                    }
                );
                continue;
            }

            var execution = PaperFillCalculator.Fill(
                order.Side,
                order.Remaining,
                order.LimitPrice,
                book,
                state.Rules,
                book.BaseScale
            );
            if (execution is null)
            {
                continue;
            }

            var fill = new TradingFill(
                Guid.NewGuid(),
                order.Id,
                "paper-"
                    + order.Id.ToString("N")
                    + "-"
                    + book.ObservedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                order.Symbol,
                order.Side,
                execution.BaseQuantity,
                order.Side == TradeSide.Buy ? book.Ask : book.Bid,
                order.Side == TradeSide.Buy ? execution.GrossThb - execution.FeeThb : execution.GrossThb,
                execution.FeeThb,
                now
            );
            ledger = TradingLedgerAccounting.Apply(ledger, fill);
            if (order.Side == TradeSide.Buy && order.Sizing is { } sizing)
            {
                ledger = ledger with
                {
                    Positions = ledger
                        .Positions.Select(position =>
                            position.Symbol == order.Symbol
                                ? position with
                                {
                                    StopPrice = sizing.Signal.StopPrice,
                                    TargetPrice =
                                        order.LimitPrice
                                        + decimal.Max(
                                            sizing.Market.Atr * 2,
                                            order.LimitPrice * state.Rules.TakeProfitBps / 10000m
                                        ),
                                    TrailingDistance = decimal.Max(
                                        sizing.Market.Atr,
                                        order.LimitPrice * state.Rules.TrailingStopBps / 10000m
                                    ),
                                    ExitPlan =
                                        position.ExitPlan
                                        ?? (
                                            order.ExitPlan is { } exitPlan
                                                ? AdaptiveExitStrategy.Anchor(exitPlan, position.OpenedAt)
                                                : null
                                        ),
                                }
                                : position
                        )
                        .ToArray(),
                };
            }

            var remaining =
                order.Remaining - (order.Side == TradeSide.Buy ? execution.GrossThb : execution.BaseQuantity);
            var dust = order.Side == TradeSide.Buy ? 0.01m : 0m;
            ledger = TradingLedgerAccounting.ReplaceOrder(
                ledger,
                order with
                {
                    Remaining = remaining,
                    Status = remaining <= dust ? "filled" : "partial",
                    LastFillObservation = book.ObservedAt,
                    LastInspectedAt = now,
                }
            );
        }

        ledger = ledger == state.Paper ? ledger : TradingLedgerValuation.Mark(ledger, books, state.Rules, now, true);
        return ledger == state.Paper
            ? state
            : await this.writer.Save(state, state with { Paper = ledger }, cancellationToken);
    }
}
