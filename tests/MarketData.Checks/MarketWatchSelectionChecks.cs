using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketWatchSelectionChecks
{
    public static async Task Run()
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
                quote_volume = rule.base_asset == "C42"
                    ? 0
                    : 1000 - int.Parse(rule.pairing_id, CultureInfo.InvariantCulture),
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
            ["C41", "C42"],
            DateTimeOffset.UnixEpoch.AddSeconds(30)
        );
        var second = MarketWatchSelection.Select(
            symbols.RootElement,
            tickers.RootElement,
            ["C41", "C42"],
            DateTimeOffset.UnixEpoch.AddSeconds(60)
        );
        if (
            first.Count > 18
            || !first.Any(item => item.Symbol == "C41_THB")
            || !first.Any(item => item.Symbol == "C42_THB")
            || first.Count(item => item.Symbol != "C41_THB" && item.Symbol != "C42_THB") > 16
        )
        {
            throw new InvalidOperationException("Required holdings must survive while candidates remain capped at 16.");
        }

        if (!first.Select(item => item.Symbol).SequenceEqual(second.Select(item => item.Symbol)))
        {
            throw new InvalidOperationException("The watch set must remain stable within a five-minute window.");
        }

        var rotated = MarketWatchSelection.Select(
            symbols.RootElement,
            tickers.RootElement,
            ["C41", "C42"],
            DateTimeOffset.UnixEpoch.AddSeconds(330)
        );
        if (
            !first
                .Skip(2)
                .Take(8)
                .Select(item => item.Symbol)
                .SequenceEqual(rotated.Skip(2).Take(8).Select(item => item.Symbol))
            || first.Skip(10).Select(item => item.Symbol).SequenceEqual(rotated.Skip(10).Select(item => item.Symbol))
        )
        {
            throw new InvalidOperationException("The rotating discovery window must change after five minutes.");
        }

        if (
            MarketWatchSelection
                .Select(symbols.RootElement, tickers.RootElement, [], DateTimeOffset.UnixEpoch)
                .Any(item => item.Symbol == "C42_THB")
        )
        {
            throw new InvalidOperationException("Zero-volume discovery must not consume a watch slot.");
        }

        if (
            MarketWatchSelection
                .Select(symbols.RootElement, tickers.RootElement, ["MISSING"], DateTimeOffset.UtcNow)
                .Any(item => item.Symbol == "MISSING_THB")
        )
        {
            throw new InvalidOperationException("Missing required holdings must not be fabricated.");
        }

        using var duplicateSymbols = JsonDocument.Parse(
            JsonSerializer.Serialize(new { result = rules.Concat([rules[41]]).ToArray() })
        );
        using var duplicateTickers = JsonDocument.Parse(
            JsonSerializer.Serialize(quotes.Concat([quotes.ElementAt(41)]).ToArray())
        );
        var duplicateSelection = MarketWatchSelection.Select(
            duplicateSymbols.RootElement,
            duplicateTickers.RootElement,
            ["C42"],
            DateTimeOffset.UnixEpoch
        );
        if (duplicateSelection.Count(item => item.Symbol == "C42_THB") != 1)
        {
            throw new InvalidOperationException(
                "Exact duplicate market rules must not drop a required zero-volume holding."
            );
        }

        using var malformedSymbols = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    result = rules
                        .Cast<object?>()
                        .Concat([
                            null,
                            42,
                            new
                            {
                                base_asset = 42,
                                quote_asset = "THB",
                                status = "active",
                                market_segment = "SPOT",
                                source = "exchange",
                                pairing_id = "999",
                            },
                        ])
                        .ToArray(),
                }
            )
        );
        var preserved = MarketWatchSelection.Select(
            malformedSymbols.RootElement,
            tickers.RootElement,
            ["C42"],
            DateTimeOffset.UnixEpoch
        );
        if (preserved.Count(item => item.Symbol == "C42_THB") != 1 || preserved.Any(item => item.Symbol == "42_THB"))
        {
            throw new InvalidOperationException("Malformed rule rows must not remove a verified required holding.");
        }

        var conflict = rules[41] with { base_asset = "c42", pairing_id = "999" };
        using var conflictingSymbols = JsonDocument.Parse(
            JsonSerializer.Serialize(new { result = rules.Append(conflict).ToArray() })
        );
        if (
            MarketWatchSelection
                .Select(conflictingSymbols.RootElement, tickers.RootElement, ["C42"], DateTimeOffset.UnixEpoch)
                .Any(item => item.Symbol == "C42_THB")
        )
        {
            throw new InvalidOperationException(
                "Conflicting market identities must not be resolved by choosing a rule."
            );
        }

        await CheckFeed(duplicateSymbols.RootElement, tickers.RootElement);
        await CheckFeed(malformedSymbols.RootElement, tickers.RootElement);

        try
        {
            MarketWatchSelection.Select(symbols.RootElement, tickers.RootElement, ["c1"], DateTimeOffset.UtcNow);
            throw new InvalidOperationException("Lowercase required symbols must be rejected.");
        }
        catch (ArgumentException) { }
    }

    private static async Task CheckFeed(JsonElement symbols, JsonElement tickers)
    {
        using var handler = new UniverseHttpHandler
        {
            SymbolsResponse = symbols.GetRawText(),
            TickerResponse = tickers.GetRawText(),
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var bus = new MarketEventBus();
        await bus.SetWatch(["C42"], CancellationToken.None);
        using var feed = new LiveMarketFeed(
            client,
            new UnusedIntradayCandleStore(),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            bus
        );
        // Exercise the real refresh without starting its public WebSocket background loops.
        var refresh =
            typeof(LiveMarketFeed).GetMethod(
                "RefreshWatch",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Watch refresh entry point was not found.");
        var pending =
            refresh.Invoke(feed, [CancellationToken.None]) as Task
            ?? throw new InvalidOperationException("Watch refresh did not return a task.");
        await pending;
        if (handler.RequestCount != 2 || !bus.WatchedSymbols.Contains("C42", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The actual feed must retain a verified required holding after refresh."
            );
        }
    }
}
