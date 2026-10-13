using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class ResearchMarketChecks
{
    public static async Task Run()
    {
        var handler = new ResearchHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        using var document = JsonDocument.Parse(
            await new BitkubResearchMarketReader(client).Read(CancellationToken.None)
        );
        var root = document.RootElement;
        var market = root.GetProperty("markets")[0];
        if (
            market.GetProperty("symbol").GetString() != "ETH_THB"
            || market.GetProperty("rules").GetProperty("priceStep").GetDecimal() != 0.1m
        )
        {
            throw new InvalidOperationException("research mapping failed");
        }

        if (
            !handler.Requests.Any(x => x.Query.Contains("sym=ETH_THB") && x.Query.Contains("lmt=5"))
            || !handler.Requests.Any(x => x.Query.Contains("sym=ETH_THB") && x.Query.Contains("lmt=10"))
        )
        {
            throw new InvalidOperationException("bounded depth/trades query failed");
        }

        using var candles = JsonDocument.Parse(
            "{\"s\":\"ok\",\"t\":[0,0,86400],\"o\":[10,10,10],\"h\":[12,12,12],\"l\":[9,9,9],\"c\":[11,11.5,11],\"v\":[1,1,1]}"
        );
        var parsed = CandleParser.Parse(candles.RootElement, 0, 172800);
        if (parsed.Count != 1 || parsed[0].Timestamp != 86400)
        {
            throw new InvalidOperationException("Conflicting candle timestamps must not invent a chosen close.");
        }

        if (CandleParser.Parse(candles.RootElement, 0, 86400).Count != 0)
        {
            throw new InvalidOperationException("Incomplete daily bars must be excluded.");
        }

        var rules = Enumerable
            .Range(1, 21)
            .Select(index => new
            {
                base_asset = "C" + index,
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToArray();
        var quotes = Enumerable
            .Range(1, 21)
            .Select(index => new
            {
                symbol = "C" + index + "_THB",
                last = index == 21 ? 150 : 100,
                highest_bid = 99,
                lowest_ask = 101,
                quote_volume = index == 21 ? 1 : 100,
                high_24_hr = index == 21 ? 200 : 101,
                low_24_hr = 99,
                percent_change = index == 21 ? 50 : 1,
            })
            .ToArray();
        using var symbols = JsonDocument.Parse(JsonSerializer.Serialize(new { result = rules }));
        using var ticker = JsonDocument.Parse(JsonSerializer.Serialize(quotes));
        var scan = MarketUniverseScanner.Scan(symbols.RootElement, ticker.RootElement);
        if (
            scan.UniverseCount != 21
            || scan.Entries.Count != 21
            || !MarketUniverseScanner.SelectCandidates(scan, 16).Any(item => item.Symbol == "C21_THB")
        )
        {
            throw new InvalidOperationException(
                "Universe scan must retain all pairs and diverse non-volume candidates."
            );
        }

        Console.WriteLine("Passed: full THB universe scan and diverse candidate selection");
        using var universeClient = new HttpClient(
            new ResearchHttpHandler
            {
                SymbolsResponse = symbols.RootElement.GetRawText(),
                TickerResponse = ticker.RootElement.GetRawText(),
            }
        )
        {
            BaseAddress = new Uri("https://test.invalid"),
        };
        var universePayload = await new BitkubResearchMarketReader(universeClient).Read(CancellationToken.None);
        using var universeDocument = JsonDocument.Parse(universePayload);
        if (
            universePayload.Length > 32000
            || universeDocument.RootElement.GetProperty("scan").GetProperty("symbols").GetArrayLength() != 21
            || universeDocument.RootElement.GetProperty("markets").GetArrayLength() != 16
        )
        {
            throw new InvalidOperationException(
                "Research must preserve every scan row while bounding detailed payload."
            );
        }

        using var missingTicker = JsonDocument.Parse(JsonSerializer.Serialize(quotes.Skip(1)));
        var missing = MarketUniverseScanner.Scan(symbols.RootElement, missingTicker.RootElement);
        if (
            missing.Entries.Count != 21
            || missing.MissingQuoteCount != 1
            || missing.Entries.Single(entry => entry.Symbol == "C1_THB").Usable
        )
        {
            throw new InvalidOperationException("Missing quotes must retain an explicitly unusable scan row.");
        }

        var changedQuote = ticker
            .RootElement[0]
            .GetRawText()
            .Replace("\"percent_change\":1", "\"percent_change\":2", StringComparison.Ordinal);
        using var conflictingTicker = JsonDocument.Parse(
            ticker.RootElement.GetRawText()[..^1] + "," + changedQuote + "]"
        );
        var conflicted = MarketUniverseScanner.Scan(symbols.RootElement, conflictingTicker.RootElement);
        if (
            conflicted.DuplicateConflictCount != 1
            || conflicted.Entries.Single(entry => entry.Symbol == "C1_THB").Usable
        )
        {
            throw new InvalidOperationException("Conflicting movement quotes must never silently select one value.");
        }

        using var invalidTicker = JsonDocument.Parse(
            ticker
                .RootElement.GetRawText()
                .Replace("\"highest_bid\":99", "\"highest_bid\":102", StringComparison.Ordinal)
        );
        if (
            MarketUniverseScanner
                .Scan(symbols.RootElement, invalidTicker.RootElement)
                .Entries.Any(entry => entry.Usable)
        )
        {
            throw new InvalidOperationException("Crossed quotes must not become trading candidates.");
        }
    }
}
