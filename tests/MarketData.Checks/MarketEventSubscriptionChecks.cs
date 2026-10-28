using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketEventSubscriptionChecks
{
    public static async Task Run()
    {
        var subscription = new MarketEventSubscription();
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 10, 100));
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 9, 90));
        var retained = await Read(subscription);
        Check(
            retained.Sequence == 10
                && retained.ObservedAt == DateTimeOffset.FromUnixTimeSeconds(100)
                && retained.Coalesced == 1,
            "Out-of-order events retain the newest sequence and payload."
        );

        subscription = new MarketEventSubscription();
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 10, 100));
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 10, 101));
        var equal = await Read(subscription);
        Check(
            equal.ObservedAt == DateTimeOffset.FromUnixTimeSeconds(100) && equal.Coalesced == 1,
            "Equal sequences retain the first payload and count coalescing."
        );

        subscription = new MarketEventSubscription();
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 10, 100));
        subscription.Publish(Event("BTC", MarketEventKind.BookChanged, 11, 101));
        var newer = await Read(subscription);
        Check(newer.Sequence == 11 && newer.Coalesced == 1, "Newer sequences replace older pending events.");

        subscription.Publish(Event("BTC", MarketEventKind.QuoteChanged, 1, 102));
        subscription.Publish(Event("ETH", MarketEventKind.BookChanged, 1, 103));
        var isolated = new[] { await Read(subscription), await Read(subscription) };
        Check(
            isolated.Any(item => item.Symbol == "BTC" && item.Kind == MarketEventKind.QuoteChanged)
                && isolated.Any(item => item.Symbol == "ETH" && item.Kind == MarketEventKind.BookChanged),
            "Different keys remain isolated and deliverable."
        );
        Console.WriteLine("Market event subscription: out-of-order coalescing and key isolation passed.");
    }

    private static MarketEvent Event(string symbol, MarketEventKind kind, long sequence, long observedAt) =>
        new(kind, symbol, sequence, DateTimeOffset.FromUnixTimeSeconds(observedAt), null, null, [], false, 0);

    private static Task<MarketEvent> Read(MarketEventSubscription subscription) =>
        subscription.Read(
            _ => throw new InvalidOperationException("Overflow sequence callback must not run."),
            [],
            TimeProvider.System,
            TimeSpan.FromSeconds(1),
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
