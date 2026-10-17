using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketWatchSelectionChecks
{
    public static void Run()
    {
        var rules = Enumerable
            .Range(1, 42)
            .Select(index => new
            {
                base_asset = "C" + index,
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = index.ToString(CultureInfo.InvariantCulture),
                freeze_buy = index == 41,
                freeze_sell = false,
            })
            .ToArray();
        var quotes = rules
            .Select(rule => new
            {
                symbol = rule.base_asset + "_THB",
                last = 100,
                highest_bid = 99,
                lowest_ask = 101,
                quote_volume = 1000 - int.Parse(rule.pairing_id, CultureInfo.InvariantCulture),
                high_24_hr = 101,
                low_24_hr = 99,
                percent_change = 1,
            })
            .ToArray();
        using var symbols = JsonDocument.Parse(JsonSerializer.Serialize(new { result = rules }));
        using var tickers = JsonDocument.Parse(JsonSerializer.Serialize(quotes));
        var first = MarketWatchSelection.Select(
            symbols.RootElement,
            tickers.RootElement,
            ["C41"],
            DateTimeOffset.UnixEpoch.AddSeconds(30)
        );
        var second = MarketWatchSelection.Select(
            symbols.RootElement,
            tickers.RootElement,
            ["C41"],
            DateTimeOffset.UnixEpoch.AddSeconds(60)
        );
        if (
            first.Count > 17
            || !first.Any(item => item.Symbol == "C41_THB")
            || first.Count(item => item.Symbol != "C41_THB") > 16
        )
        {
            throw new InvalidOperationException("Required holdings must survive while candidates remain capped at 16.");
        }

        if (first.Select(item => item.Symbol).SequenceEqual(second.Select(item => item.Symbol)))
        {
            throw new InvalidOperationException("Candidate watch rotation must change the selected window.");
        }

        if (
            MarketWatchSelection
                .Select(symbols.RootElement, tickers.RootElement, ["MISSING"], DateTimeOffset.UtcNow)
                .Any(item => item.Symbol == "MISSING_THB")
        )
        {
            throw new InvalidOperationException("Missing required holdings must not be fabricated.");
        }

        try
        {
            MarketWatchSelection.Select(symbols.RootElement, tickers.RootElement, ["c1"], DateTimeOffset.UtcNow);
            throw new InvalidOperationException("Lowercase required symbols must be rejected.");
        }
        catch (ArgumentException) { }
    }
}
