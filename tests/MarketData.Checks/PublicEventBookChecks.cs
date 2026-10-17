using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class PublicEventBookChecks
{
    public static void Run()
    {
        using var ruleDocument = JsonDocument.Parse(
            "{\"pairing_id\":42,\"price_step\":0.1,\"price_scale\":1,\"base_asset_scale\":6,\"quote_asset_scale\":2,\"min_quote_size\":10,\"freeze_buy\":false,\"freeze_sell\":false}"
        );
        var book = new PublicEventBook("BTC", 42, ruleDocument.RootElement);
        if (
            book.Apply(
                "{\"event\":\"bidschanged\",\"pairing_id\":42,\"data\":[[100,1000,0.01,0,false,false]]}",
                DateTimeOffset.UtcNow
            )
            is not null
        )
        {
            throw new InvalidOperationException("A one-sided book must remain incomplete.");
        }

        var observedAt = DateTimeOffset.UtcNow;
        book.Apply(
            "{\"event\":\"bidschanged\",\"pairing_id\":42,\"data\":[[100,1000,0.01,0,false,false]]}",
            observedAt
        );
        var complete = book.Apply(
            "{\"event\":\"askschanged\",\"pairing_id\":42,\"data\":[[101,1000,0.02,0,false,false]]}",
            observedAt.AddSeconds(2)
        );
        if (
            complete is null
            || complete.Bid != 1000m
            || complete.Ask != 1000m
            || complete.ObservedAt != observedAt
            || complete.BidQuantity != 0.01m
            || complete.AskQuantity != 0.02m
        )
        {
            throw new InvalidOperationException("A valid two-sided book must retain exchange quantities.");
        }

        if (
            book.Apply(
                "{\"event\":\"bidschanged\",\"pairing_id\":41,\"data\":[[100,1000,0.01,0,false,false]]}",
                DateTimeOffset.UtcNow
            )
            is not null
        )
        {
            throw new InvalidOperationException("A mismatched pairing must be rejected.");
        }

        if (book.Apply("{\"event\":\"bidschanged\",\"pairing_id\":42,\"data\":[]}", DateTimeOffset.UtcNow) is not null)
        {
            throw new InvalidOperationException("An empty side must invalidate the book.");
        }

        if (
            book.Apply("{\"event\":\"tradeschanged\",\"pairing_id\":42,\"data\":[[],[],[]]}", DateTimeOffset.UtcNow)
            is not null
        )
        {
            throw new InvalidOperationException("Trade-only events must not invent a book.");
        }

        book.Apply("{\"event\":\"bidschanged\",\"data\":[[100,1000,0.01]]}", observedAt);
        book.Reset();
        if (book.Apply("{\"event\":\"askschanged\",\"data\":[[100,1000,0.02]]}", observedAt) is not null)
        {
            throw new InvalidOperationException("Reconnect must not combine a prior socket's side with a new side.");
        }
    }
}
