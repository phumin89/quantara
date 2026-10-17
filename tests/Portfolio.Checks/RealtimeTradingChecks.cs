using System.Diagnostics;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class RealtimeTradingChecks
{
    public static async Task Run()
    {
        using var fixture = new AutonomousFixture();
        using var dispatcher = new TradingEventDispatcher();
        fixture.Store.State = fixture.Store.State with
        {
            Mode = TradingMode.Paper,
            Paper = fixture.Store.State.Paper with
            {
                FundingThb = 5000,
                CashThb = 4000,
                Positions = [new TradingHolding("ETH", 10, 1000, fixture.Time.Now, 100)],
            },
        };
        var engine = new TradingEngine(
            fixture.Store,
            fixture.Markets,
            fixture.Exchange,
            fixture.Account,
            fixture.Gate,
            fixture.Time,
            autonomous: fixture.Monitor,
            adaptiveRisk: fixture.Risk,
            events: dispatcher
        );
        var stalled = new TaskCompletionSource();
        fixture.Evidence.BeforeRead = stalled;
        await engine.Process(new(TradingEventKind.Research, "", 0, fixture.Time.Now), CancellationToken.None);
        var samples = new List<double>();
        try
        {
            for (var index = 0; index < 100; index++)
            {
                var book = fixture.Book("ETH") with { Bid = 100.1m, Ask = 100.2m, Bars = [] };
                var item = new TradingMarketEvent(
                    TradingEventKind.BookChanged,
                    "ETH",
                    index + 1,
                    fixture.Time.Now,
                    book: book
                );
                var started = Stopwatch.GetTimestamp();
                dispatcher.Enqueue(item, true);
                var received =
                    await dispatcher.Dequeue(CancellationToken.None)
                    ?? throw new InvalidOperationException("Missing event.");
                await engine.Process(received, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            var stop = fixture.Book("ETH") with { Bid = 98, Ask = 98.1m, Bars = [] };
            var exit = new TradingMarketEvent(TradingEventKind.BookChanged, "ETH", 101, fixture.Time.Now, book: stop);
            dispatcher.Enqueue(exit, true);
            var first =
                await dispatcher.Dequeue(CancellationToken.None)
                ?? throw new InvalidOperationException("Missing exit.");
            await engine.Process(first, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            Check(
                fixture.Store.State.Paper.Fills.Any(fill => fill.Side == TradeSide.Sell)
                    && fixture.Store.State.Paper.Positions.All(position => position.Quantity == 0),
                "The first fresh quote exits the Paper position without a candle or ten-second wait."
            );
            Check(
                fixture.Exchange.Submissions == 0 && dispatcher.Snapshot().LastExitEvaluationAt is not null,
                "Paper events never submit to a real broker and expose exit evaluation time."
            );
            await engine.Process(exit, CancellationToken.None);
            Check(
                fixture.Store.State.Paper.Fills.Count(fill => fill.Side == TradeSide.Sell) == 1,
                "Repeated exit events cannot sell the position twice."
            );
        }
        finally
        {
            stalled.SetResult();
        }

        var ordered = samples.Order().ToArray();
        var p50 = ordered[49];
        var p95 = ordered[94];
        Check(p95 < 2000, "Event-to-local-decision p95 exceeds the two-second test target.");
        Console.WriteLine(
            FormattableString.Invariant(
                $"Realtime Paper: 100 fresh owned events during stalled research; event-to-local-cycle p50={p50:0.000}ms p95={p95:0.000}ms. Fake storage/quotes; exchange/API latency excluded."
            )
        );
        await IntrabarGuard();
        await UnknownRecovery();
        await OtherExitPaths();
        await FreshUniverse();
    }

    private static async Task IntrabarGuard()
    {
        using var fixture = new AutonomousFixture();
        using var dispatcher = new TradingEventDispatcher();
        await fixture.Activate();
        var engine = new TradingEngine(
            fixture.Store,
            fixture.Markets,
            fixture.Exchange,
            fixture.Account,
            fixture.Gate,
            fixture.Time,
            autonomous: fixture.Monitor,
            adaptiveRisk: fixture.Risk,
            events: dispatcher
        );
        var book = fixture.Book("ETH");
        dispatcher.Enqueue(
            new(TradingEventKind.BookChanged, "ETH", 1, fixture.Time.Now, book: book with { Bars = [] }),
            false
        );
        await engine.Process(new(TradingEventKind.BookChanged, "ETH", 1, fixture.Time.Now), CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 0,
            "Intrabar quotes do not authorize new entries in the existing strategy."
        );
        var closed = new TradingMarketEvent(
            TradingEventKind.CandleClosed,
            "ETH",
            2,
            fixture.Time.Now,
            candleAt: book.Bars[^1].Timestamp
        );
        await engine.Process(closed, CancellationToken.None);
        await engine.Process(closed, CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Count == 1,
            "Completed-candle evidence produces one fake broker intent, including duplicate delivery."
        );

        var intent = fixture.Store.State.Live.Orders.Single();
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        var fill = new TradingFill(
            Guid.NewGuid(),
            intent.Id,
            "event-partial-fill",
            "ETH",
            TradeSide.Buy,
            1,
            110,
            110,
            0.28m,
            fixture.Time.Now
        );
        fixture.Exchange.Observation = order => new("partial", order.BrokerOrderId, order.Amount - 110.28m, [fill]);
        await engine.PrepareReconciliation(CancellationToken.None);
        var reconciliation = new TradingMarketEvent(TradingEventKind.Reconcile, "", 0, fixture.Time.Now);
        await engine.Process(reconciliation, CancellationToken.None);
        Check(
            fixture.Store.State.Live.Fills.Count == 1 && fixture.Store.State.Live.Orders.Single().Status == "partial",
            "Read-only broker inspection applies an exact partial fill through the serial event path."
        );
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        await engine.PrepareReconciliation(CancellationToken.None);
        await engine.Process(reconciliation, CancellationToken.None);
        Check(
            fixture.Store.State.Live.Fills.Count == 1 && fixture.Exchange.Submissions == 1,
            "Repeated partial observations do not duplicate a fill or resubmit an order."
        );
    }

    private static async Task UnknownRecovery()
    {
        using var fixture = new AutonomousFixture();
        using var dispatcher = new TradingEventDispatcher();
        await fixture.Activate();
        fixture.Exchange.FailSubmission = true;
        var engine = EventEngine(fixture, dispatcher);
        var book = fixture.Book("ETH");
        dispatcher.Observe(
            new(TradingEventKind.BookChanged, "ETH", 1, fixture.Time.Now, book: book with { Bars = [] })
        );
        var closed = new TradingMarketEvent(
            TradingEventKind.CandleClosed,
            "ETH",
            2,
            fixture.Time.Now,
            candleAt: book.Bars[^1].Timestamp
        );
        await engine.Process(closed, CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Single().Status == "unknown",
            "An uncertain fake POST is durably unknown."
        );
        fixture.Exchange.Observation = _ => new("unknown", null, null, []);
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        var restarted = EventEngine(fixture, dispatcher);
        await restarted.PrepareReconciliation(CancellationToken.None);
        await restarted.Process(new(TradingEventKind.Reconcile, "", 0, fixture.Time.Now), CancellationToken.None);
        await restarted.Process(closed, CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Single().Status == "unknown",
            "Restart, reconnect and duplicate candles never retry an uncertain submission."
        );
    }

    private static TradingEngine EventEngine(AutonomousFixture fixture, TradingEventDispatcher dispatcher) =>
        new(
            fixture.Store,
            fixture.Markets,
            fixture.Exchange,
            fixture.Account,
            fixture.Gate,
            fixture.Time,
            autonomous: fixture.Monitor,
            adaptiveRisk: fixture.Risk,
            events: dispatcher
        );

    private static async Task OtherExitPaths()
    {
        foreach (var reason in new[] { "Volatility take profit", "Volatility trailing stop", "Time exit" })
        {
            using var fixture = new AutonomousFixture();
            using var dispatcher = new TradingEventDispatcher();
            var holding = new TradingHolding("ETH", 10, 1000, fixture.Time.Now, 100)
            {
                StopPrice = 95,
                TargetPrice = 120,
                TrailingDistance = 1,
            };
            holding = reason switch
            {
                "Volatility trailing stop" => holding with { PeakBid = 103 },
                "Time exit" => holding with { OpenedAt = fixture.Time.Now.AddHours(-3) },
                _ => holding,
            };
            fixture.Store.State = fixture.Store.State with
            {
                Mode = TradingMode.Paper,
                Paper = fixture.Store.State.Paper with { FundingThb = 5000, CashThb = 4000, Positions = [holding] },
            };
            var bid = reason == "Volatility take profit" ? 120m : 100m;
            var book = fixture.Book("ETH") with { Bid = bid, Ask = bid + .1m, Bars = [] };
            var item = new TradingMarketEvent(TradingEventKind.BookChanged, "ETH", 1, fixture.Time.Now, book: book);
            dispatcher.Enqueue(item, true);
            var engine = EventEngine(fixture, dispatcher);
            await engine.Process(item, CancellationToken.None);
            Check(
                fixture.Store.State.Paper.Orders.Any(order => order.Side == TradeSide.Sell && order.Reason == reason)
                    && fixture.Store.State.Paper.Fills.Count == 1
                    && fixture.Exchange.Submissions == 0,
                reason + " uses the first fresh quote without completed-candle context."
            );
        }
    }

    private static async Task FreshUniverse()
    {
        using var fixture = new AutonomousFixture();
        fixture.Store.State = fixture.Store.State with { Mode = TradingMode.Paper };
        var symbols = Enumerable
            .Range(1, 40)
            .Select(index => "C" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        fixture.Markets.BeforeUniverseRead = () =>
        {
            fixture.Time.Now = fixture.Time.Now.AddMilliseconds(1);
            fixture.Markets.Universe = new(fixture.Time.Now, symbols, 40, 0, null);
        };
        for (var index = 0; index < 3; index++)
        {
            fixture.Markets.Books = symbols.Select(symbol => fixture.Book(symbol, false)).ToArray();
            await fixture.Engine.Tick(CancellationToken.None);
            fixture.Time.Now = fixture.Time.Now.AddSeconds(61);
        }

        Check(
            fixture.Store.State.Autonomy?.Markets.Count == 40,
            "A newly fetched universe is timed after receipt and preserves scan progress across refreshes."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
