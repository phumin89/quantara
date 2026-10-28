using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingOrderReconciler(
    ITradingStore store,
    ITradingExchange exchange,
    TimeProvider time,
    AutonomousRiskService? adaptiveRisk
)
{
    private readonly TradingStateWriter writer = new(store);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TradingPreparedObservation> ready = new();

    internal async Task Prepare(CancellationToken cancellationToken)
    {
        var state = await store.Read(cancellationToken);
        var activeIds = state
            .Live.Orders.Where(TradingLedgerAccounting.Unresolved)
            .Select(order => order.Id)
            .ToHashSet();
        foreach (var id in this.ready.Keys.Where(id => !activeIds.Contains(id)))
        {
            this.ready.TryRemove(id, out _);
        }

        var now = time.GetUtcNow();
        var due = state
            .Live.Orders.Where(order =>
                TradingLedgerAccounting.Unresolved(order)
                && order.Status != "pending"
                && (order.LastInspectedAt is null || now - order.LastInspectedAt >= TimeSpan.FromSeconds(30))
            )
            .OrderBy(order => order.LastInspectedAt ?? DateTimeOffset.MinValue)
            .ThenBy(order => order.CreatedAt)
            .Take(4);
        // Publish one bounded concurrent batch before slow peers can age out healthy evidence.
        await Task.WhenAll(
            due.Select(async order =>
            {
                var observation = await this.Inspect(order, cancellationToken);
                this.ready[order.Id] = new(order, observation, time.GetUtcNow());
            })
        );
    }

    internal async Task<TradingState> Reconcile(
        TradingState state,
        DateTimeOffset now,
        bool preparedOnly,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var original in state
                .Live.Orders.Where(order => TradingLedgerAccounting.Unresolved(order) && order.Status != "pending")
                .ToArray()
        )
        {
            if (original.LastInspectedAt is not null && now - original.LastInspectedAt < TimeSpan.FromSeconds(30))
            {
                continue;
            }

            TradingOrderObservation observation;
            DateTimeOffset inspectedAt;
            if (preparedOnly)
            {
                if (
                    !this.ready.TryRemove(original.Id, out var prepared)
                    || System.Text.Json.JsonSerializer.Serialize(prepared.Intent)
                        != System.Text.Json.JsonSerializer.Serialize(original)
                    || prepared.ObservedAt > now
                    || now - prepared.ObservedAt > TimeSpan.FromSeconds(30)
                )
                {
                    continue;
                }

                observation = prepared.Observation;
                inspectedAt = prepared.ObservedAt;
            }
            else
            {
                observation = await this.Inspect(original, cancellationToken);
                inspectedAt = time.GetUtcNow();
            }

            TradingState next;
            try
            {
                next = Apply(state, original, observation, inspectedAt);
            }
            catch (Exception error)
                when (error is InvalidDataException or InvalidOperationException or OverflowException)
            {
                // Reject the entire observation; no partial fills or broker action escape validation.
                next = Apply(state, original, Unavailable(original), inspectedAt);
            }

            var accountChanged =
                next.Live.Fills.Count != state.Live.Fills.Count
                || next.Live.Orders.Single(order => order.Id == original.Id).Status != original.Status;
            // Persistence failures must escape; only broker evidence is isolated per order.
            state = await this.writer.Save(state, next, cancellationToken);
            if (accountChanged)
            {
                adaptiveRisk?.Invalidate();
            }
        }

        return state;
    }

    private async Task<TradingOrderObservation> Inspect(TradingIntent intent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await exchange.Inspect(intent, timeout.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(intent);
        }
    }

    private static TradingOrderObservation Unavailable(TradingIntent intent) =>
        new("unknown", intent.BrokerOrderId, null, [], "Broker inspection unavailable; no order was resent.");

    private static TradingState Apply(
        TradingState state,
        TradingIntent original,
        TradingOrderObservation observation,
        DateTimeOffset inspectedAt
    )
    {
        if (
            observation.Status is not ("unknown" or "submitted" or "partial" or "filled" or "cancelled")
            || observation.Error is { Length: > 500 }
        )
        {
            throw new InvalidDataException("Invalid reconciliation state.");
        }

        if (
            observation.Status != "unknown"
            && (
                observation.BrokerOrderId is null
                || observation.Remaining is null
                || observation.Remaining < 0
                || observation.Remaining > original.Amount
            )
        )
        {
            throw new InvalidDataException("Incomplete broker order reconciliation.");
        }

        var ledger = state.Live;
        foreach (
            var fill in observation
                .Fills.OrderBy(fill => fill.OccurredAt)
                .ThenBy(fill => fill.BrokerFillId, StringComparer.Ordinal)
        )
        {
            if (
                fill.IntentId != original.Id
                || fill.Symbol != original.Symbol
                || fill.Side != original.Side
                || fill.OccurredAt < original.CreatedAt.AddMinutes(-1)
                || fill.OccurredAt > inspectedAt.AddSeconds(5)
                || fill.Side == TradeSide.Buy && fill.Price > original.LimitPrice
                || fill.Side == TradeSide.Sell && fill.Price < original.LimitPrice
            )
            {
                throw new InvalidDataException("Fill identity, time or limit mismatch.");
            }

            ledger = TradingLedgerAccounting.Apply(ledger, fill);
            if (fill.Side == TradeSide.Buy && original.Sizing is { } sizing)
            {
                ledger = ledger with
                {
                    Positions = ledger
                        .Positions.Select(position =>
                            position.Symbol == original.Symbol
                                ? position with
                                {
                                    StopPrice = position.ExitPlan?.EffectiveStop ?? sizing.Signal.StopPrice,
                                    TargetPrice =
                                        original.LimitPrice
                                        + decimal.Max(
                                            sizing.Market.Atr * 2,
                                            original.LimitPrice * state.Rules.TakeProfitBps / 10000m
                                        ),
                                    TrailingDistance = decimal.Max(
                                        sizing.Market.Atr,
                                        original.LimitPrice * state.Rules.TrailingStopBps / 10000m
                                    ),
                                    ExitPlan =
                                        position.ExitPlan
                                        ?? (
                                            original.ExitPlan is { } exitPlan
                                                ? AdaptiveExitStrategy.Anchor(exitPlan, position.OpenedAt)
                                                : null
                                        ),
                                }
                                : position
                        )
                        .ToArray(),
                };
            }
        }

        var fills = ledger.Fills.Where(fill => fill.IntentId == original.Id).ToArray();
        var used =
            original.Side == TradeSide.Buy
                ? fills.Sum(fill => fill.GrossThb + fill.FeeThb)
                : fills.Sum(fill => fill.Quantity);
        if (
            used > original.Amount + (original.Side == TradeSide.Buy ? 0.00000001m : 0.000000000000000001m)
            || observation.Status == "filled" && (fills.Length == 0 || observation.Remaining != 0)
        )
        {
            throw new InvalidDataException("Fill totals do not reconcile to submitted amount.");
        }

        var intent = original with
        {
            Status = observation.Status,
            BrokerOrderId = observation.BrokerOrderId ?? original.BrokerOrderId,
            Remaining = observation.Remaining ?? original.Remaining,
            LastInspectedAt = inspectedAt,
            LastInspectionError = observation.Status == "unknown" ? observation.Error : null,
        };
        if (!TradingLedgerAccounting.Active(intent))
        {
            intent = intent with { CancellationState = intent.CancellationState is null ? null : "confirmed" };
        }

        var notices = ledger
            .Fills.Where(fill => !state.Live.Fills.Any(previous => previous.BrokerFillId == fill.BrokerFillId))
            .Select(fill => TradingFillNotice.Create(fill, inspectedAt))
            .Where(notice => !state.Alerts.Any(alert => alert.Key == notice.Key));
        return state with
        {
            Live = TradingLedgerAccounting.ReplaceOrder(ledger, intent),
            Alerts = state.Alerts.Concat(notices).ToArray(),
        };
    }
}
