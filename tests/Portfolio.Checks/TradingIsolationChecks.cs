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
            unavailable == failed with { Status = "unknown", LastInspectedAt = time.Now }
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
