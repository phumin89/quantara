using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingEngineChecks
{
    public static async Task Run()
    {
        await Paper();
        await Live();
        await Reconciliation();
        await Unknown();
        await Cancellation();
        await CancellationClaimExpiry();
        await Authority();
        await Exposure();
        await ClaimExpiry();
        await DelayedQuotes();
        await LastBaseUnit();
        await ExecutionPriceRounding();
        Console.WriteLine("Trading engine checks passed.");
    }

    private static async Task ExecutionPriceRounding()
    {
        var time = new OrderCheckTime();
        var initial = Initial(time);
        var rules = initial.Rules with { SlippageBps = 20 };
        var state = initial with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Paper = initial.Paper with
            {
                CashThb = 100,
                Positions = [new TradingHolding("BTC", 1000m, 20m, time.Now.AddMinutes(-121), 0.02m)],
            },
        };
        var store = new FakeTradingStore(state);
        var markets = new FakeTradingMarketReader([
            Book(time, bid: 0.01m, ask: 0.01m, askQuantity: 1000m) with
            {
                BidQuantity = 1000m,
                PriceStep = 0.01m,
            },
        ]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single() is { Side: TradeSide.Sell, LimitPrice: 0.01m, Status: "filled" }
                && store.State.Paper.Positions.Single().Quantity == 0m,
            "Sell at the minimum tick uses a nonzero executable limit."
        );
        Check(exchange.Submissions == 0, "Paper rounding regression never reaches a broker.");

        var coarseRules = initial.Rules with { SlippageBps = 20 };
        var coarse = initial with
        {
            Rules = coarseRules,
            RulesHash = TradingRuleHash.Compute(coarseRules),
            Paper = initial.Paper with
            {
                CashThb = 100,
                Positions = [new TradingHolding("BTC", 1m, 100, time.Now.AddMinutes(-121), 100)],
            },
        };
        var coarseStore = new FakeTradingStore(coarse);
        var coarseMarkets = new FakeTradingMarketReader([
            Book(time, bid: 100, ask: 100, askQuantity: 1) with
            {
                PriceStep = 1,
            },
        ]);
        await Engine(coarseStore, coarseMarkets, new FakeTradingExchange(), time, gate).Tick(CancellationToken.None);
        Check(
            coarseStore.State.Paper.Orders.Single() is { Side: TradeSide.Sell, LimitPrice: 100m, Status: "filled" }
                && coarseStore.State.Paper.Positions.Single().Quantity == 0m,
            "Coarse sell ticks round up to the permitted price."
        );
    }

    private static async Task Paper()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time, askQuantity: .2m)]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, markets, exchange, time, gate);
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Status == "partial"
                && store.State.Paper.Fills.Count == 1
                && store.State.Paper.Positions.Single().Quantity == .2m,
            "Paper breakout must persist and partially fill only observed ask liquidity."
        );
        await engine.Tick(CancellationToken.None);
        Check(store.State.Paper.Fills.Count == 1, "The same book observation cannot be consumed twice.");
        time.Now = time.Now.AddSeconds(1);
        markets.Books = [Book(time)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Status == "filled"
                && store.State.Paper.Fills.Count == 2
                && store.State.Paper.CashThb >= 900,
            "Fresh book liquidity must complete the remaining paper amount without overspending."
        );
        time.Now = time.Now.AddSeconds(1);
        markets.Books = [Book(time, bid: 110, ask: 110.1m)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Count == 1
                && store.State.Paper.Orders.Count(order => order.Side == TradeSide.Buy) == 1,
            "An already-held breakout position must not buy again."
        );
        time.Now = time.Now.AddSeconds(1);
        markets.Books = [Book(time, bid: 113, ask: 113.1m)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Any(order =>
                order.Side == TradeSide.Sell && order.Reason == "Take profit" && order.Status == "filled"
            )
                && store.State.Paper.Fills.Any(fill => fill.Side == TradeSide.Sell && fill.RealizedNetThb > 0)
                && exchange.Submissions == 0,
            "Paper take-profit must realize a strategy sell without touching the broker."
        );
        Check(
            store.State.Paper.Positions.Single().Quantity * 113m >= 10,
            "A capped take-profit must leave a tradable remainder rather than below-minimum dust."
        );
        time.Now = time.Now.AddMinutes(5);
        markets.Books = [Book(time, bid: 113, ask: 113.1m)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Positions.Single().Quantity == 0 && exchange.Submissions == 0,
            "The next candle closes the remaining take-profit position within the order cap."
        );

        var stopped = Initial(time);
        store.State = stopped with
        {
            Paper = stopped.Paper with
            {
                CashThb = 945,
                Positions = [new TradingHolding("BTC", .5m, 55, time.Now, 110)],
            },
        };
        markets.Books = [Book(time, bid: 108, ask: 108.1m)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Reason == "Stop loss"
                && store.State.Paper.Orders.Single().Status == "filled"
                && store.State.Paper.Positions.Single().Quantity == 0
                && store.State.Paper.Fills.Single().RealizedNetThb < 0,
            "Paper stop-loss must close an owned quantity and record its realized loss."
        );

        store.State = stopped with
        {
            Paper = stopped.Paper with
            {
                CashThb = 945,
                Positions = [new TradingHolding("BTC", .5m, 55, time.Now, 110)],
            },
        };
        markets.Books = [Book(time, bid: 113, ask: 113.1m)];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Reason == "Take profit"
                && store.State.Paper.Orders.Single().Status == "filled"
                && store.State.Paper.Positions.Single().Quantity == 0
                && store.State.Paper.Fills.Single().RealizedNetThb > 0,
            "A position below the per-order cap must completely close at take profit."
        );
    }

    private static async Task Live()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time)]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Activate(store, markets, exchange, time, gate);
        exchange.BeforeSend = (intent, cancellation) =>
            Check(
                !cancellation && store.State.Live.Orders.Single(order => order.Id == intent.Id).Status == "submitting",
                "A durable submission claim must precede the broker POST."
            );
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Mode == TradingMode.AutoLive
                && store.State.Live.Orders.Single().Status == "submitted"
                && exchange.Submissions == 1,
            "A reviewed fake mandate allows exactly one engine submission and no restart retry."
        );
    }

    private static async Task Reconciliation()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time)]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Activate(store, markets, exchange, time, gate);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        var intent = store.State.Live.Orders.Single();
        time.Now = time.Now.AddSeconds(31);
        markets.Books = [Book(time)];
        var first = new TradingFill(
            Guid.NewGuid(),
            intent.Id,
            "fake-fill-1",
            "BTC",
            TradeSide.Buy,
            49.9m / 110m,
            110m,
            49.9m,
            .1m,
            time.Now,
            GrossFeeThb: .2m,
            FeeCreditThb: .1m
        );
        exchange.Observation = _ => new("partial", intent.BrokerOrderId, 50, [first]);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Live.Fills.Count == 1
                && store.State.Live.CashThb == 950
                && store.State.Live.Orders.Single().Status == "partial",
            "Verified partial live fills must update cash once from gross plus actual net fee."
        );
        time.Now = time.Now.AddSeconds(31);
        markets.Books = [Book(time)];
        var second = first with { Id = Guid.NewGuid(), BrokerFillId = "fake-fill-2", OccurredAt = time.Now };
        exchange.Observation = _ => new("filled", intent.BrokerOrderId, 0, [first, second]);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Live.Fills.Count == 2
                && store.State.Live.CashThb == 900
                && store.State.Live.Orders.Single().Status == "filled"
                && store.State.Live.Fills.Sum(fill => fill.FeeThb) == .2m
                && store.State.Live.Fills.Sum(fill => fill.GrossFeeThb) == .4m
                && store.State.Live.Fills.Sum(fill => fill.FeeCreditThb) == .2m
                && exchange.Submissions == 1,
            "Cumulative broker history must deduplicate fills and retain gross fee, fee credit and actual net fee without another submission."
        );
    }

    private static async Task Unknown()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time)]);
        var exchange = new FakeTradingExchange
        {
            FailSubmission = true,
            Observation = _ => new("unknown", null, null, []),
        };
        using var gate = new SemaphoreSlim(1);
        await Activate(store, markets, exchange, time, gate);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Live.Orders.Single().Status == "unknown" && exchange.Submissions == 1,
            "An uncertain broker POST must remain durably unknown."
        );
        time.Now = time.Now.AddSeconds(31);
        markets.Books = [Book(time)];
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Live.Orders.Single().Status == "unknown" && exchange.Submissions == 1,
            "Read-only reconciliation and restart must never repeat an uncertain submission."
        );
    }

    private static async Task Cancellation()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time)]);
        var exchange = new FakeTradingExchange { FailCancellation = true };
        using var gate = new SemaphoreSlim(1);
        await Activate(store, markets, exchange, time, gate);
        var intent = Intent(store.State, time, "submitted") with
        {
            CreatedAt = time.Now.AddMinutes(-16),
            BrokerOrderId = "fake-existing",
            LastInspectedAt = time.Now,
        };
        store.State = store.State with { Live = store.State.Live with { Orders = [intent] } };
        exchange.BeforeSend = (order, cancellation) =>
            Check(
                cancellation
                    && store.State.Live.Orders.Single(item => item.Id == order.Id).CancellationState == "claimed",
                "A durable cancellation claim must precede its broker POST."
            );
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Live.Orders.Single().CancellationState == "unknown" && exchange.Cancellations == 1,
            "Uncertain cancellation must retain its durable claim."
        );
        time.Now = time.Now.AddSeconds(31);
        markets.Books = [Book(time)];
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            exchange.Cancellations == 1 && exchange.Submissions == 0,
            "Restart must not replay an uncertain cancellation."
        );
    }

    private static async Task CancellationClaimExpiry()
    {
        foreach (var outcome in new[] { "unchanged", "new-revision", "changed-order", "receipt-failed" })
        {
            var time = new OrderCheckTime();
            var store = new FakeTradingStore(Initial(time));
            var markets = new FakeTradingMarketReader([Book(time)]);
            var exchange = new FakeTradingExchange();
            using var gate = new SemaphoreSlim(1);
            await Activate(store, markets, exchange, time, gate);
            var intent = Intent(store.State, time, "submitted") with
            {
                CreatedAt = time.Now.AddMinutes(-16),
                BrokerOrderId = "fake-existing",
                LastInspectedAt = time.Now,
            };
            store.State = store.State with { Live = store.State.Live with { Orders = [intent] } };
            store.BeforeSave = candidate =>
            {
                if (candidate.Live.Orders.Single().CancellationState == "claimed")
                {
                    store.BeforeSave = null;
                    time.Now = time.Now.AddDays(2);
                }
            };
            store.AfterSave = candidate =>
            {
                if (candidate.Live.Orders.Single().CancellationState != "claimed")
                {
                    return;
                }

                store.AfterSave = null;
                if (outcome == "receipt-failed")
                {
                    throw new IOException("Fake cancellation claim acknowledgement failure.");
                }

                if (outcome == "new-revision")
                {
                    store.State = candidate with { Revision = candidate.Revision + 1 };
                }
                else if (outcome == "changed-order")
                {
                    store.State = candidate with
                    {
                        Live = candidate.Live with
                        {
                            Orders = [candidate.Live.Orders.Single() with { LimitPrice = intent.LimitPrice + 1 }],
                        },
                    };
                }
            };
            await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
            var retained = store.State.Live.Orders.Single();
            Check(
                exchange.Cancellations == 0 && exchange.Submissions == 0,
                outcome + ": expiry after a cancellation claim prevents any broker send."
            );
            Check(
                retained.Id == intent.Id
                    && retained.Status == "submitted"
                    && retained.BrokerOrderId == intent.BrokerOrderId
                    && retained.Remaining == intent.Remaining
                    && retained.CancellationState == (outcome == "unchanged" ? null : "claimed")
                    && retained.LimitPrice == (outcome == "changed-order" ? intent.LimitPrice + 1 : intent.LimitPrice)
                    && store.State.Live.Fills.Count == 0,
                outcome + ": only the same-call unchanged claim may be cleared; the live broker order remains retained."
            );
            markets.Books = [Book(time)];
            await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
            Check(
                store.State.Mode == TradingMode.Stopped
                    && exchange.Cancellations == 0
                    && exchange.Submissions == 0
                    && store.State.Live.Orders.Single().CancellationState
                        == (outcome == "unchanged" ? null : "claimed"),
                outcome + ": restart with expired authority never replays cancellation or resolves retained ambiguity."
            );
        }
    }

    private static async Task Authority()
    {
        foreach (var emergency in new[] { false, true })
        {
            var time = new OrderCheckTime();
            var store = new FakeTradingStore(Initial(time));
            var markets = new FakeTradingMarketReader([Book(time)]);
            var exchange = new FakeTradingExchange();
            using var gate = new SemaphoreSlim(1);
            await Activate(store, markets, exchange, time, gate);
            store.State = store.State with
            {
                Live = store.State.Live with { Orders = [Intent(store.State, time, "pending")] },
            };
            await Control(store, markets, exchange, time, gate).Disable(emergency, CancellationToken.None);
            await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
            Check(
                store.State.Mode == TradingMode.Stopped
                    && store.State.Mandate?.RevokedAt is not null
                    && store.State.Live.Orders.Single().Status == "cancelled"
                    && exchange.Submissions == 0,
                "Owner disable/emergency stop must revoke authority and cancel unsubmitted intents."
            );
        }

        var expiryTime = new OrderCheckTime();
        var expiryStore = new FakeTradingStore(Initial(expiryTime));
        var expiryMarkets = new FakeTradingMarketReader([Book(expiryTime)]);
        var expiryExchange = new FakeTradingExchange();
        using var expiryGate = new SemaphoreSlim(1);
        await Activate(expiryStore, expiryMarkets, expiryExchange, expiryTime, expiryGate);
        expiryStore.State = expiryStore.State with
        {
            Live = expiryStore.State.Live with { Orders = [Intent(expiryStore.State, expiryTime, "pending")] },
        };
        expiryTime.Now = expiryTime.Now.AddHours(2);
        expiryMarkets.Books = [Book(expiryTime)];
        await Engine(expiryStore, expiryMarkets, expiryExchange, expiryTime, expiryGate).Tick(CancellationToken.None);
        Check(
            expiryStore.State.Mode == TradingMode.Stopped
                && expiryExchange.Submissions == 0
                && expiryStore.State.Live.Orders.Single().Status == "cancelled",
            "Expired mandate must stop before any pending order can submit."
        );
    }

    private static async Task Exposure()
    {
        var time = new OrderCheckTime();
        var state = Initial(time);
        var rules = state.Rules with { Symbols = ["BTC", "ETH"] };
        state = state with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
        var reserved = Intent(state, time, "pending") with { Symbol = "ETH", Amount = 90, Remaining = 90 };
        var store = new FakeTradingStore(
            state with
            {
                Paper = state.Paper with { CashThb = 100, Orders = [reserved] },
            }
        );
        var markets = new FakeTradingMarketReader([Book(time), Book(time) with { Symbol = "ETH", AskQuantity = 0 }]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Count == 1
                && !store.State.Paper.Orders.Any(order => order.Symbol == "BTC")
                && store.State.Paper.CashThb == 100,
            "Cash reserved by an outstanding buy cannot fund another entry."
        );

        var liveStore = new FakeTradingStore(Initial(time));
        markets.Books = [Book(time)];
        await Activate(liveStore, markets, exchange, time, gate);
        liveStore.State = liveStore.State with
        {
            Live = liveStore.State.Live with
            {
                CashThb = 978,
                Positions = [new TradingHolding("BTC", .2m, 22, time.Now, 110)],
            },
        };
        exchange.AvailableBase = .1m;
        markets.Books = [Book(time, bid: 108, ask: 108.1m)];
        await Engine(liveStore, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            exchange.Submissions == 0
                && liveStore.State.Live.Positions.Single().Quantity == .2m
                && liveStore.State.Fault is not null,
            "Broker available-base shortage must block a sell without changing owned quantity."
        );
    }

    private static async Task ClaimExpiry()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var markets = new FakeTradingMarketReader([Book(time)]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Activate(store, markets, exchange, time, gate);
        store.BeforeSave = candidate =>
        {
            if (candidate.Live.Orders.Any(order => order.Status == "submitting"))
            {
                time.Now = time.Now.AddDays(2);
            }
        };
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            exchange.Submissions == 0
                && store.State.Live.Orders.Single() is { Status: "cancelled", BrokerOrderId: null },
            "Expiry after a successful claim records a known-unsent cancellation before POST."
        );
        store.BeforeSave = null;
        await Engine(store, markets, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Mode == TradingMode.Stopped && exchange.Submissions == 0,
            "Expiry must disarm the mode even when broker reconciliation cannot complete."
        );
    }

    private static async Task DelayedQuotes()
    {
        var time = new OrderCheckTime();
        var store = new FakeTradingStore(Initial(time));
        var market = new FakeTradingMarketReader([Book(time) with { ObservedAt = time.Now.AddSeconds(1) }])
        {
            BeforeRead = () => time.Now = time.Now.AddSeconds(2),
        };
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        await Engine(store, market, exchange, time, gate).Tick(CancellationToken.None);
        Check(
            store.State.Fault is null && store.State.Paper.Orders.Count == 1,
            "Quotes observed during a network request are evaluated against receipt time, not request start."
        );
        market.Books = [Book(time) with { ObservedAt = time.Now.AddSeconds(1) }];
        var activated = await Control(store, market, exchange, time, gate)
            .Activate(
                new string('a', 64),
                store.State.RulesHash,
                time.Now.AddHours(1),
                "enable-auto-trading",
                CancellationToken.None
            );
        Check(
            activated.Mode == TradingMode.AutoLive && exchange.Submissions == 0,
            "Activation accepts verified quotes timestamped during the request without sending an order."
        );
    }

    private static async Task LastBaseUnit()
    {
        var time = new OrderCheckTime();
        var initial = Initial(time);
        var intent = Intent(initial, time, "pending") with
        {
            Side = TradeSide.Sell,
            Amount = 1,
            Remaining = 1,
            LimitPrice = 100,
        };
        var store = new FakeTradingStore(
            initial with
            {
                Paper = initial.Paper with
                {
                    CashThb = 900,
                    Positions = [new TradingHolding("BTC", 1, 100, time.Now, 100)],
                    Orders = [intent],
                },
            }
        );
        var markets = new FakeTradingMarketReader([
            Book(time, bid: 100, ask: 100.1m) with
            {
                BidQuantity = .99999999m,
                Bars = [],
            },
        ]);
        using var gate = new SemaphoreSlim(1);
        var engine = Engine(store, markets, new FakeTradingExchange(), time, gate);
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Status == "partial"
                && store.State.Paper.Positions.Single().Quantity == .00000001m,
            "An unfilled base unit remains partial and owned; it is never declared fully sold."
        );
        time.Now = time.Now.AddSeconds(1);
        markets.Books = [Book(time, bid: 100, ask: 100.1m) with { Bars = [] }];
        await engine.Tick(CancellationToken.None);
        Check(
            store.State.Paper.Orders.Single().Status == "filled" && store.State.Paper.Positions.Single().Quantity == 0,
            "Only a subsequent verified paper fill closes the last base unit."
        );
    }

    private static TradingState Initial(OrderCheckTime time)
    {
        var state = TradingStateFactory.Create(time.Now);
        var rules = state.Rules with { FeeBps = 0, SlippageBps = 0, CooldownMinutes = 0 };
        return state with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
    }

    private static TradingBook Book(
        OrderCheckTime time,
        decimal bid = 109.9m,
        decimal ask = 110m,
        decimal askQuantity = 10m
    )
    {
        var end = time.Now.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index => new TradingBar(
                end - (51 - index) * 300,
                100,
                index == 50 ? 111 : 101,
                99,
                index == 50 ? 110 : 100,
                index == 50 ? 2 : 1
            ))
            .ToArray();
        return new TradingBook("BTC", time.Now, bid, ask, 10, askQuantity, bars);
    }

    private static TradingIntent Intent(TradingState state, OrderCheckTime time, string status) =>
        new(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100,
            110,
            100,
            time.Now,
            time.Now.ToUnixTimeSeconds() / 300 * 300 - 300,
            state.RulesHash,
            "fixture",
            status
        );

    private static TradingEngine Engine(
        FakeTradingStore store,
        FakeTradingMarketReader markets,
        FakeTradingExchange exchange,
        OrderCheckTime time,
        SemaphoreSlim gate
    ) => new(store, markets, exchange, new AccountContext(new string('a', 64)), gate, time);

    private static TradingControl Control(
        FakeTradingStore store,
        FakeTradingMarketReader markets,
        FakeTradingExchange exchange,
        OrderCheckTime time,
        SemaphoreSlim gate
    ) =>
        new(
            store,
            exchange,
            markets,
            new AccountContext(new string('a', 64)),
            new TradingControlOptions("owner-token"),
            gate,
            time
        );

    private static Task<TradingState> Activate(
        FakeTradingStore store,
        FakeTradingMarketReader markets,
        FakeTradingExchange exchange,
        OrderCheckTime time,
        SemaphoreSlim gate
    ) =>
        Control(store, markets, exchange, time, gate)
            .Activate(
                new string('a', 64),
                store.State.RulesHash,
                time.Now.AddHours(1),
                "enable-auto-trading",
                CancellationToken.None
            );

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
