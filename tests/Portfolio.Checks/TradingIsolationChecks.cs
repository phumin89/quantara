using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingIsolationChecks
{
    public static async Task Run()
    {
        await FailedInspectionPreservesPeers(false);
        await FailedInspectionPreservesPeers(true);
        await ChangedIntentRejectsPreparedEvidence();
        await SlowPeersPreserveHealthyEvidence();
        await InspectionDiagnostics();
        Console.WriteLine("Trading reconciliation isolation checks passed.");
    }

    private static async Task FailedInspectionPreservesPeers(bool malformed)
    {
        var time = new OrderCheckTime();
        var initial = TradingStateFactory.Create(time.Now);
        var failed = Intent(initial, time, "BTC");
        var healthy = Intent(initial, time, "ETH");
        var store = new FakeTradingStore(
            initial with
            {
                Mode = TradingMode.Stopped,
                Live = initial.Live with { CashThb = 1000, FundingThb = 1000, Orders = [failed, healthy] },
            }
        );
        var exchange = new FakeTradingExchange
        {
            Observation = order =>
                order.Id != failed.Id ? new("cancelled", order.BrokerOrderId, order.Amount, [])
                : malformed
                    ? new(
                        "filled",
                        order.BrokerOrderId,
                        0,
                        [
                            new(
                                Guid.NewGuid(),
                                order.Id,
                                "valid-first",
                                "BTC",
                                TradeSide.Buy,
                                1,
                                100,
                                100,
                                0,
                                time.Now
                            ),
                            new(
                                Guid.NewGuid(),
                                Guid.NewGuid(),
                                "invalid-second",
                                "BTC",
                                TradeSide.Buy,
                                1,
                                100,
                                100,
                                0,
                                time.Now
                            ),
                        ]
                    )
                : throw new HttpRequestException("Isolated fake inspection failure."),
        };
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, exchange, time, gate);
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        Check(
            store.State.Live.Orders.Single(order => order.Id == healthy.Id).Status == "cancelled",
            "One unavailable order must not prevent a peer from reconciling."
        );
        var unavailable = store.State.Live.Orders.Single(order => order.Id == failed.Id);
        Check(
            unavailable
                == failed with
                {
                    Status = "unknown",
                    LastInspectedAt = time.Now,
                    LastInspectionError = "Broker inspection unavailable; no order was resent.",
                }
                && store.State.Live.Fills.Count == 0
                && store.State.Live.CashThb == 1000
                && exchange.Submissions == 0
                && exchange.Cancellations == 0,
            "Failed inspection preserves its exact retained intent without broker side effects."
        );
    }

    private static async Task ChangedIntentRejectsPreparedEvidence()
    {
        var time = new OrderCheckTime();
        var initial = TradingStateFactory.Create(time.Now);
        var intent = Intent(initial, time, "BTC");
        var store = new FakeTradingStore(
            initial with
            {
                Mode = TradingMode.Stopped,
                Live = initial.Live with { Orders = [intent] },
            }
        );
        var exchange = new FakeTradingExchange
        {
            Observation = order => new("cancelled", order.BrokerOrderId, order.Amount, []),
        };
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, exchange, time, gate);
        await engine.PrepareReconciliation(CancellationToken.None);
        var changed = intent with { LimitPrice = intent.LimitPrice + 1, CancellationState = "claimed" };
        store.State = store.State with { Live = store.State.Live with { Orders = [changed] } };
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        Check(
            store.State.Live.Orders.Single() == changed,
            "Prepared observations bind the entire retained intent, including price and cancellation claim."
        );
    }

    private static async Task SlowPeersPreserveHealthyEvidence()
    {
        var time = new OrderCheckTime();
        var initial = TradingStateFactory.Create(time.Now);
        var healthy = Intent(initial, time, "BTC");
        var orders = new[] { healthy }
            .Concat(Enumerable.Range(0, 4).Select(index => Intent(initial, time, $"PEER{index}")))
            .ToArray();
        var store = new FakeTradingStore(
            initial with
            {
                Mode = TradingMode.Stopped,
                Live = initial.Live with { Orders = orders },
            }
        );
        var exchange = new FakeTradingExchange
        {
            Observation = order =>
            {
                if (order.Id == healthy.Id)
                {
                    return new("cancelled", order.BrokerOrderId, order.Amount, []);
                }

                time.Now = time.Now.AddSeconds(10);
                throw new HttpRequestException("Isolated slow peer.");
            },
        };
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, exchange, time, gate);
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        Check(
            store.State.Live.Orders.Single(order => order.Id == healthy.Id).Status == "cancelled",
            "Slow peer failures must not make healthy prepared evidence expire indefinitely."
        );
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        Check(
            store.State.Live.Orders.All(order => order.LastInspectedAt is not null),
            "A bounded inspection batch must eventually inspect every due order."
        );
        Check(
            exchange.Submissions == 0 && exchange.Cancellations == 0,
            "Inspection batches must never send broker actions."
        );
    }

    private static async Task InspectionDiagnostics()
    {
        var time = new OrderCheckTime();
        var initial = TradingStateFactory.Create(time.Now);
        var intent = Intent(initial, time, "BTC");
        const string message = "No exact broker match found. Outcome remains unknown; no order was resent.";
        var store = new FakeTradingStore(
            initial with
            {
                Mode = TradingMode.Stopped,
                Live = initial.Live with { Orders = [intent] },
            }
        );
        var exchange = new FakeTradingExchange
        {
            Observation = order => new("unknown", order.BrokerOrderId, null, [], message),
        };
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, exchange, time, gate);
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        var inspected = store.State.Live.Orders.Single();
        Check(
            inspected.LastInspectionError == message
                && inspected.LastInspectedAt == time.Now
                && inspected.Reason == intent.Reason
                && inspected.Status == "unknown",
            "Inspection diagnostics are retained separately from the original trading reason."
        );
        var options = PortfolioJson.CreateOptions();
        var legacyJson = System.Text.Json.JsonSerializer.Serialize(intent, options);
        Check(
            !legacyJson.Contains("LastInspectionError", StringComparison.Ordinal),
            "Absent diagnostics preserve legacy JSON."
        );
        var restored = System.Text.Json.JsonSerializer.Deserialize<TradingIntent>(
            System.Text.Json.JsonSerializer.Serialize(inspected, options),
            options
        );
        Check(restored == inspected, "Inspection diagnostics survive persistence without changing the intent.");

        time.Now = time.Now.AddSeconds(31);
        exchange.Observation = order => new("cancelled", order.BrokerOrderId, order.Amount, []);
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(new(TradingEventKind.Reconcile, "", 0, time.Now), CancellationToken.None);
        Check(
            store.State.Live.Orders.Single() is { Status: "cancelled", LastInspectionError: null }
                && exchange.Submissions == 0
                && exchange.Cancellations == 0,
            "A verified terminal result clears stale diagnostics without broker side effects."
        );
    }

    private static TradingIntent Intent(TradingState state, OrderCheckTime time, string symbol) =>
        new(
            Guid.NewGuid(),
            symbol,
            TradeSide.Buy,
            100,
            100,
            100,
            time.Now,
            0,
            state.RulesHash,
            "isolated regression",
            "submitted",
            "broker-" + symbol
        );

    private static TradingEngine Engine(
        FakeTradingStore store,
        FakeTradingExchange exchange,
        OrderCheckTime time,
        SemaphoreSlim gate
    ) => new(store, new FakeTradingMarketReader([]), exchange, new AccountContext(new string('a', 64)), gate, time);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
