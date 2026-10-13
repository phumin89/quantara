using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class PublicTickerChecks
{
    public static void Run()
    {
        const string sample =
            "{\"stream\":\"market.ticker.thb_btc\",\"last\":2801093.36,\"lowestAsk\":2801105.95,\"highestBid\":2800562.82,\"isFrozen\":0}";
        var now = DateTimeOffset.UtcNow;
        var quote = PublicTickerParser.Parse(sample, now);
        if (quote?.Symbol != "BTC_THB" || quote.Bid != 2800562.82m || quote.ObservedAt != now)
        {
            throw new InvalidOperationException(
                "Actual public stream shape was not accepted with receive-time provenance."
            );
        }

        foreach (
            var invalid in new[]
            {
                sample.Replace("\"stream\"", "\"channel\"", StringComparison.Ordinal),
                sample.Replace("\"isFrozen\":0", "\"isFrozen\":1", StringComparison.Ordinal),
                sample.Replace("2800562.82", "2900000", StringComparison.Ordinal),
                "{}",
                "not json",
            }
        )
        {
            if (PublicTickerParser.Parse(invalid, now) is not null)
            {
                throw new InvalidOperationException("Malformed, frozen or crossed ticker accepted.");
            }
        }
    }
}
