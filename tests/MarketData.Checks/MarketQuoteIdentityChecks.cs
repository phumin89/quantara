using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketQuoteIdentityChecks
{
    private const string coin =
        """{"base_asset":"BTC","quote_asset":"THB","name":"Bitcoin","source":"exchange","status":"active","market_segment":"SPOT","pairing_id":"1"}""";

    private const string quote =
        """{"symbol":"BTC_THB","last":100,"highest_bid":99,"lowest_ask":101,"quote_volume":1000,"percent_change":2,"high_24_hr":110,"low_24_hr":90}""";

    public static async Task Run()
    {
        await RestQuotes();
        await Metadata();
        Scanner();
        Console.WriteLine("Market quote identity checks passed.");
    }

    private static async Task RestQuotes()
    {
        var equivalent = await Quotes(
            """[{"symbol":"BTC_THB","last":100.25},{"symbol":"btc_THB","last":"100.2500"},{"symbol":"BTC_THB","last":"1.0025e2"},{"symbol":"BTC_THB","last":10025e-2}]"""
        );
        Check(equivalent.SequenceEqual([new MarketQuote("BTC", 100.25m)]), "Equivalent REST prices dedupe.");
        foreach (var changed in new[] { "100.26", "100.250000000000000001" })
        {
            foreach (var reverse in new[] { false, true })
            {
                var first = Ticker("BTC_THB", reverse ? changed : "100.25");
                var second = Ticker("btc_THB", reverse ? "100.25" : changed);
                await Reject(
                    () => Quotes("[" + first + "," + second + "]"),
                    "Conflicting REST prices in either order."
                );
            }
        }

        var filtered = await Quotes(
            """[{"symbol":"BTC_THB","last":100.25},{"symbol":"ETH_USDT","last":10},{"symbol":"BTC_THB","last":0},{"symbol":"BTC_THB","last":-1}]"""
        );
        Check(filtered.SequenceEqual([new MarketQuote("BTC", 100.25m)]), "Filtering precedes dedupe.");
        foreach (
            var symbol in new[]
            {
                "_BTC_THB",
                "BTC__THB",
                "BTC_THB_",
                "BTC_THB_EXTRA",
                "BTC-USD_THB",
                " BTC_THB",
                "฿TC_THB",
                new string('A', 31) + "_THB",
            }
        )
        {
            await NotAdmitted(() => Quotes("[" + Ticker(symbol, "100") + "]"), "Invalid REST pair: " + symbol);
        }
    }

    private static async Task Metadata()
    {
        var duplicate = coin.Replace("\"BTC\"", "\"btc\"", StringComparison.Ordinal);
        var coins = await Coins("[" + coin + "," + duplicate + "]");
        Check(coins.SequenceEqual([new MarketCoin("BTC", "Bitcoin", "exchange")]), "Metadata dedupes.");
        foreach (var value in new[] { "Bitcoin", "exchange" })
        {
            var conflict = coin.Replace(value, value + "-changed", StringComparison.Ordinal);
            foreach (var reverse in new[] { false, true })
            {
                await Reject(
                    () => Coins("[" + (reverse ? conflict + "," + coin : coin + "," + conflict) + "]"),
                    "Conflicting metadata in either order."
                );
            }
        }

        foreach (var symbol in new[] { "", " BTC", "BTC_USD", "BTC-USD", "ข", new string('A', 31) })
        {
            var invalid = coin.Replace("\"BTC\"", JsonSerializer.Serialize(symbol), StringComparison.Ordinal);
            await NotAdmitted(() => Coins("[" + invalid + "]"), "Invalid coin token: " + symbol);
        }

        var unsupported = coin.Replace("\"BTC\"", "\"ETH\"", StringComparison.Ordinal)
            .Replace("\"THB\"", "\"USDT\"", StringComparison.Ordinal);
        Check(
            (await Coins("[" + coin + "," + unsupported + "]")).Count == 1,
            "Unsupported coin quote remains filtered."
        );
    }

    private static void Scanner()
    {
        const string represented =
            """{"symbol":"btc_THB","last":"1e2","highest_bid":"99.000","lowest_ask":"1.01e2","quote_volume":"1000.0","percent_change":"2.00","high_24_hr":"110.000","low_24_hr":"9e1"}""";
        foreach (var reverse in new[] { false, true })
        {
            var same = Scan(reverse ? represented : quote, reverse ? quote : represented);
            Check(
                same.Entries.Count == 1
                    && same.Entries[0].Usable
                    && same.DuplicateConflictCount == 0
                    && MarketUniverseScanner.SelectCandidates(same, 16).Count == 1,
                "Equivalent scanner rows yield one usable sample."
            );
        }

        var invalidTokens = Scan(
            quote.Replace("\"percent_change\":2", "\"percent_change\":\"bad-a\"", StringComparison.Ordinal),
            quote.Replace("\"percent_change\":2", "\"percent_change\":\"bad-b\"", StringComparison.Ordinal)
        );
        Check(
            invalidTokens.DuplicateConflictCount == 1 && !invalidTokens.Entries.Single().Usable,
            "Distinct invalid quote tokens retain their conflict count."
        );

        (string Field, decimal Value)[] changes =
        [
            ("last", 101m),
            ("highest_bid", 100m),
            ("lowest_ask", 102m),
            ("quote_volume", 1001m),
            ("percent_change", 3m),
            ("high_24_hr", 111m),
            ("low_24_hr", 89m),
            ("last", 100.000000000000000001m),
        ];
        foreach (var change in changes)
        {
            var changed =
                JsonNode.Parse(quote)?.AsObject() ?? throw new InvalidOperationException("Missing scanner fixture.");
            changed[change.Field] = change.Value;
            foreach (var reverse in new[] { false, true })
            {
                var conflict = Scan(reverse ? changed.ToJsonString() : quote, reverse ? quote : changed.ToJsonString());
                Check(
                    conflict.DuplicateConflictCount == 1 && !conflict.Entries.Single().Usable,
                    "A differing exact scanner field conflicts: " + change.Field
                );
            }
        }

        foreach (var field in changes.Select(change => change.Field).Distinct(StringComparer.Ordinal))
        {
            var missing =
                JsonNode.Parse(quote)?.AsObject() ?? throw new InvalidOperationException("Missing scanner fixture.");
            missing.Remove(field);
            var unknown =
                JsonNode.Parse(quote)?.AsObject() ?? throw new InvalidOperationException("Missing scanner fixture.");
            unknown[field] = null;
            var zero =
                JsonNode.Parse(quote)?.AsObject() ?? throw new InvalidOperationException("Missing scanner fixture.");
            zero[field] = 0m;
            var unavailable = Scan(missing.ToJsonString(), unknown.ToJsonString());
            Check(
                unavailable.DuplicateConflictCount == 0 && !unavailable.Entries.Single().Usable,
                "Unknown scanner fields remain unusable: " + field
            );
            var conflict = Scan(unknown.ToJsonString(), zero.ToJsonString());
            Check(
                conflict.DuplicateConflictCount == 1 && !conflict.Entries.Single().Usable,
                "Unknown never becomes numeric zero: " + field
            );
        }

        (string Field, decimal Value)[] unusable =
        [
            ("last", 0m),
            ("highest_bid", 0m),
            ("lowest_ask", 98m),
            ("quote_volume", -1m),
            ("high_24_hr", 99m),
            ("low_24_hr", 101m),
        ];
        foreach (var invalid in unusable)
        {
            var row =
                JsonNode.Parse(quote)?.AsObject() ?? throw new InvalidOperationException("Missing scanner fixture.");
            row[invalid.Field] = invalid.Value;
            Check(
                !Scan(row.ToJsonString(), row.ToJsonString()).Entries.Single().Usable,
                "Existing usability guard remains: " + invalid.Field
            );
        }
    }

    private static MarketScanResult Scan(string first, string second)
    {
        using var symbols = JsonDocument.Parse("{\"result\":[" + coin + "]}");
        using var quotes = JsonDocument.Parse("[" + first + "," + second + "]");
        return MarketUniverseScanner.Scan(symbols.RootElement, quotes.RootElement);
    }

    private static string Ticker(string symbol, string price) =>
        "{\"symbol\":" + JsonSerializer.Serialize(symbol) + ",\"last\":" + price + "}";

    private static async Task<IReadOnlyList<MarketQuote>> Quotes(string ticker)
    {
        using var client = new HttpClient(new FakeBitkubHandler("{\"result\":[]}", ticker))
        {
            BaseAddress = new Uri("https://test.invalid"),
        };
        return await new BitkubMarketDataReader(client).ReadQuotes(CancellationToken.None);
    }

    private static async Task<IReadOnlyList<MarketCoin>> Coins(string records)
    {
        using var client = new HttpClient(new FakeBitkubHandler("{\"code\":0,\"result\":" + records + "}", "[]"))
        {
            BaseAddress = new Uri("https://test.invalid"),
        };
        return await new BitkubMarketDataReader(client).ReadCoins(CancellationToken.None);
    }

    private static async Task Reject<T>(Func<Task<T>> action, string message)
    {
        try
        {
            _ = await action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static async Task NotAdmitted<T>(Func<Task<IReadOnlyList<T>>> action, string message)
    {
        try
        {
            Check((await action()).Count == 0, message);
        }
        catch (InvalidDataException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
