using System.Text.Json;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketEventChecks
{
    public static async Task Run()
    {
        await MarketEventSubscriptionChecks.Run();
        var bus = new MarketEventBus();
        await bus.SetWatch(["ETH"], CancellationToken.None);
        await bus.SetWatched(["BTC", "ETH"], CancellationToken.None);
        Check(
            bus.RequiredSymbols.SequenceEqual(["ETH"]) && bus.WatchedSymbols.Count == 2,
            "Owned watch requirements stay separate from bounded candidates."
        );
        Check(
            !MarketEventAccess.Allows(null, "test")
                && !MarketEventAccess.Allows("secret", "wrong")
                && MarketEventAccess.Allows("secret", "secret"),
            "Private stream authentication fails closed."
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = bus.Read(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        await using var second = bus.Read(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var readFirst = first.MoveNextAsync().AsTask();
        var readSecond = second.MoveNextAsync().AsTask();
        var now = DateTimeOffset.UtcNow;
        bus.Publish(MarketEventKind.BookChanged, "BTC", now);
        Check(
            await readFirst && await readSecond && first.Current.Sequence == second.Current.Sequence,
            "SSE clients receive fanout instead of competing for events."
        );
        for (var index = 0; index < 10000; index++)
        {
            bus.Publish(MarketEventKind.BookChanged, "BTC", now);
        }

        Check(
            await first.MoveNextAsync() && first.Current.Sequence == 10001 && first.Current.Coalesced >= 9999,
            "A burst retains the latest per-symbol snapshot and counts coalescing."
        );
        var candle = now.ToUnixTimeSeconds() / 300 * 300 - 300;
        Check(
            bus.Publish(MarketEventKind.CandleClosed, "BTC", now, candleAt: candle)
                && !bus.Publish(MarketEventKind.CandleClosed, "BTC", now, candleAt: candle),
            "Completed candle triggers are deduplicated."
        );
        for (var index = 0; index < 100; index++)
        {
            var symbol = "C" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bus.Publish(MarketEventKind.BookChanged, symbol, now);
            bus.Publish(MarketEventKind.QuoteChanged, symbol, now);
            bus.Publish(MarketEventKind.FeedUnavailable, symbol, now);
        }

        Check(
            await first.MoveNextAsync() && first.Current.Dirty,
            "Overflow explicitly marks a symbol for a fresh read."
        );
        var heartbeat = new MarketEventBus(heartbeat: TimeSpan.FromMilliseconds(5));
        await using var quiet = heartbeat.Read(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Check(
            await quiet.MoveNextAsync() && quiet.Current.Kind == MarketEventKind.Heartbeat,
            "A quiet stream retains a bounded heartbeat."
        );
        var invalid = false;
        try
        {
            await bus.SetWatch(["eth"], cancellation.Token);
        }
        catch (ArgumentException)
        {
            invalid = true;
        }

        Check(invalid, "Watch identities are validated.");
        var nullSymbols =
            JsonSerializer.Deserialize<string[]>("[null]")
            ?? throw new InvalidOperationException("The malformed watch fixture must deserialize.");
        foreach (var required in new[] { true, false })
        {
            invalid = false;
            try
            {
                if (required)
                {
                    await bus.SetWatch(nullSymbols, cancellation.Token);
                }
                else
                {
                    await bus.SetWatched(nullSymbols, cancellation.Token);
                }
            }
            catch (ArgumentException)
            {
                invalid = true;
            }

            Check(invalid, "Null watch identities are rejected as invalid input.");
            Check(
                bus.RequiredSymbols.SequenceEqual(["ETH"]) && bus.WatchedSymbols.SequenceEqual(["BTC", "ETH"]),
                "Rejected watch identities preserve the previous required and active symbols."
            );
        }

        PublicEventBookChecks.Run();
        await MarketWatchSelectionChecks.Run();
        await MarketSessionChecks.Run();
        await MarketFeedIdentityChecks.Run();
        Console.WriteLine(
            "Market events: private auth, fanout, 10000-event coalescing, dirty overflow, candle dedupe, heartbeat and verified books passed."
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
