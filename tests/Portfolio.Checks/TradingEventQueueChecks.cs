using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingEventQueueChecks
{
    public static async Task Run()
    {
        using var queue = new TradingEventDispatcher(2);
        var now = DateTimeOffset.UtcNow;
        var eventTiming = new TradingMarketEvent(TradingEventKind.BookChanged, "BTC", 1, now);
        var coalescedTiming = eventTiming.WithCoalesced(4);
        Check(
            coalescedTiming.GetReceivedTimestamp() == eventTiming.GetReceivedTimestamp(),
            "Coalescing preserves the original local receive time for decision latency."
        );
        Check(
            !System
                .Text.Json.JsonSerializer.Serialize(coalescedTiming)
                .Contains("ReceivedTimestamp", StringComparison.OrdinalIgnoreCase),
            "Process-local stopwatch timing never enters the market event wire contract."
        );
        queue.Enqueue(new TradingMarketEvent(TradingEventKind.QuoteChanged, "BTC", 1, now), false);
        queue.Enqueue(new TradingMarketEvent(TradingEventKind.QuoteChanged, "BTC", 2, now), false);
        queue.Enqueue(new TradingMarketEvent(TradingEventKind.BookChanged, "ETH", 1, now), true);
        var first = await queue.Dequeue(CancellationToken.None);
        if (first?.Symbol != "ETH")
        {
            throw new InvalidOperationException("Owned events must have priority.");
        }

        var second = await queue.Dequeue(CancellationToken.None);
        if (second?.Symbol != "BTC")
        {
            throw new InvalidOperationException("Latest symbol event must be retained.");
        }

        queue.Enqueue(new TradingMarketEvent(TradingEventKind.BookChanged, "BTC", 3, now), false);
        queue.Enqueue(new TradingMarketEvent(TradingEventKind.BookChanged, "BTC", 4, now), false);
        var latest = await queue.Dequeue(CancellationToken.None);
        if (latest?.Sequence != 4)
        {
            throw new InvalidOperationException("Book events must coalesce to latest.");
        }

        var book = new TradingBook("BTC", now, 1, 2, 3, 4, []);
        queue.Enqueue(new TradingMarketEvent(TradingEventKind.BookChanged, "BTC", 5, now, book: book), false);
        if (queue.LatestBook("BTC") != book)
        {
            throw new InvalidOperationException("Latest book must be retained.");
        }

        using var burst = new TradingEventDispatcher(1);
        burst.Enqueue(new(TradingEventKind.BookChanged, "ETH", 1, now), true);
        burst.Enqueue(new(TradingEventKind.BookChanged, "XRP", 1, now), true);
        burst.Enqueue(new(TradingEventKind.CandleClosed, "BTC", 2, now, candleAt: 0), false);
        burst.EnqueueMaintenance(true);
        burst.EnqueueMaintenance();
        burst.Enqueue(new(TradingEventKind.Research, "", 0, now), false);
        Check(
            (await burst.Dequeue(CancellationToken.None)) is { Symbol: "XRP", Dirty: true },
            "Overflow keeps owned exits dirty for an immediate fresh read."
        );
        Check((await burst.Dequeue(CancellationToken.None))?.Symbol == "ETH", "Owned exits precede reconciliation.");
        Check(
            (await burst.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.Reconcile,
            "Reconciliation survives a saturated event queue."
        );
        Check(
            (await burst.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.Maintenance,
            "Expiry and heartbeat work survives a saturated event queue."
        );
        Check(
            (await burst.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.Research,
            "Research follows the owned exit and maintenance lanes."
        );
        Check(burst.Snapshot().Dropped == 2, "Overflow counts discarded/coalesced triggers honestly.");
        using var priority = new TradingEventDispatcher();
        priority.Enqueue(new(TradingEventKind.CandleClosed, "BTC", 2, now), false);
        priority.EnqueueMaintenance(true);
        priority.Enqueue(new(TradingEventKind.BookChanged, "ETH", 1, now), true);
        Check((await priority.Dequeue(CancellationToken.None))?.Symbol == "ETH", "Exit priority.");
        Check(
            (await priority.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.Reconcile,
            "Reconcile priority."
        );
        Check(
            (await priority.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.CandleClosed,
            "Entry priority."
        );
        priority.SetFeed(true, now, ["BTC"]);
        priority.Observe(new(TradingEventKind.BookChanged, "BTC", 3, now, book: book, coalesced: 100));
        priority.Observe(
            new(
                TradingEventKind.BookChanged,
                "BTC",
                4,
                now,
                book: book with
                {
                    ObservedAt = now.AddSeconds(-1),
                },
                coalesced: 100
            )
        );
        Check(
            priority.LatestBook("BTC") == book && priority.Snapshot().Coalesced == 100,
            "Older quotes do not overwrite fresh evidence and transport counters are not double-counted."
        );
        priority.SetFeed(false, now, ["BTC"]);
        Check(priority.LatestBook("BTC") is null, "Disconnect invalidates cached prices.");
        var snapshotStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        priority.Observe(new(TradingEventKind.BookChanged, "BTC", 5, now, book: book with { Bid = 0.9m }));
        priority.Enqueue(new(TradingEventKind.BookChanged, "BTC", 0, now, book: book), true, snapshotStarted);
        Check(
            priority.LatestBook("BTC")?.Bid == 0.9m,
            "A delayed HTTP snapshot cannot overwrite a stream quote received after that read began."
        );
        using var busy = new TradingEventDispatcher();
        for (var sequence = 1; sequence <= 32; sequence++)
        {
            busy.Enqueue(new(TradingEventKind.BookChanged, "ETH", sequence, now), true);
        }

        busy.EnqueueMaintenance(true);
        for (var index = 0; index < 16; index++)
        {
            Check(
                (await busy.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.BookChanged,
                "Received owned quotes retain exit priority within a bounded burst."
            );
        }

        Check(
            (await busy.Dequeue(CancellationToken.None))?.Kind == TradingEventKind.Reconcile,
            "Continuous owned traffic must not starve due reconciliation."
        );
        Console.WriteLine(
            "Event dispatcher: bounded burst, dirty exits, deadline lanes, priority, cache freshness and disconnect passed."
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
