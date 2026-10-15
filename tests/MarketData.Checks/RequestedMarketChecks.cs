using System.Text.Json;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class RequestedMarketChecks
{
    public static async Task Run()
    {
        var bases = Enumerable
            .Range(1, 243)
            .Select(index => "C" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture))
            .Append("KUB")
            .ToArray();
        var rules = bases.Select(
            (symbol, index) =>
                new
                {
                    base_asset = symbol,
                    quote_asset = "THB",
                    status = "active",
                    market_segment = "SPOT",
                    source = "exchange",
                    pairing_id = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    min_quote_size = 10,
                    price_step = .01m,
                    price_scale = 2,
                    base_asset_scale = 8,
                    quote_asset_scale = 2,
                    freeze_buy = false,
                    freeze_sell = false,
                }
        );
        var quotes = bases.Select(symbol => new
        {
            symbol = symbol + "_THB",
            last = 100,
            highest_bid = 99.99m,
            lowest_ask = 100.01m,
            quote_volume = symbol == "KUB" ? 1 : 100000,
            high_24_hr = 101,
            low_24_hr = 99,
            percent_change = symbol == "KUB" ? 0 : 1,
        });
        using var symbolDocument = JsonDocument.Parse(JsonSerializer.Serialize(new { result = rules }));
        using var quoteDocument = JsonDocument.Parse(JsonSerializer.Serialize(quotes));
        var scan = MarketUniverseScanner.Scan(symbolDocument.RootElement, quoteDocument.RootElement);
        Check(
            !MarketUniverseScanner.SelectCandidates(scan, 16).Any(entry => entry.Symbol == "KUB_THB"),
            "Fixture candidate must be outside the default shortlist."
        );
        var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300 * 300;
        var timestamps = Enumerable.Range(0, 121).Select(index => end - (120 - index) * 300).ToArray();
        var prices = Enumerable.Range(0, 121).Select(index => 10.123456789012345m + index * .001m).ToArray();
        var handler = new ResearchHttpHandler
        {
            SymbolsResponse = symbolDocument.RootElement.GetRawText(),
            TickerResponse = quoteDocument.RootElement.GetRawText(),
            DepthResponse = "{\"error\":0,\"asks\":[[100.01,20]],\"bids\":[[99.99,20]]}",
            TradesResponse = "[{\"timestamp\":1,\"price\":100,\"amount\":1}]",
            IntradayHistoryResponse = JsonSerializer.Serialize(
                new
                {
                    s = "ok",
                    t = timestamps,
                    o = prices,
                    h = prices.Select(price => price + .1m),
                    l = prices.Select(price => price - .1m),
                    c = prices,
                    v = prices.Select(_ => 20m),
                }
            ),
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var reader = new BitkubResearchMarketReader(client);
        var payload = await reader.Read(CancellationToken.None, ["kub", "C241", "C242"]);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var series = root.GetProperty("intraday").GetProperty("series");
        var requestedTo = long.Parse(
            handler
                .Requests.First(uri => uri.Query.Contains("resolution=5", StringComparison.Ordinal))
                .Query.Split('&')
                .Single(part => part.StartsWith("to=", StringComparison.Ordinal))[3..],
            System.Globalization.CultureInfo.InvariantCulture
        );
        var expectedLatest = Math.Min(end, requestedTo - 300);
        var expectedPriceIndex = (int)((expectedLatest - (end - 120 * 300)) / 300);
        Check(
            payload.Length <= 32000 && root.GetProperty("scan").GetProperty("symbols").GetArrayLength() == 244,
            "Targeted evidence must retain the full universe and respect its budget."
        );
        Check(
            root.GetProperty("markets")[0].GetProperty("symbol").GetString() == "KUB_THB"
                && root.GetProperty("markets")[0].GetProperty("depth").ValueKind != JsonValueKind.Null,
            "Requested candidate must receive detailed quote/rules/depth first."
        );
        Check(
            series.GetArrayLength() == 3 && series[0].GetProperty("symbol").GetString() == "KUB_THB",
            "All requested candle series must survive budgeting."
        );
        Check(
            root.GetProperty("intraday").GetProperty("available").GetBoolean()
                && !root.GetProperty("intraday").GetProperty("connected").GetBoolean(),
            "REST evidence must not claim a WebSocket connection."
        );
        foreach (var item in series.EnumerateArray())
        {
            var candles = item.GetProperty("candles");
            Check(
                candles.GetArrayLength() == 21
                    && candles[20][0].GetInt64() == expectedLatest
                    && candles[20][4].GetDecimal() == prices[expectedPriceIndex],
                "Only completed lossless candles are returned."
            );
            Check(
                item.GetProperty("adaptive").GetProperty("available").GetBoolean(),
                "Requested completed source bars support adaptive assessment."
            );
        }

        foreach (
            var invalid in new[]
            {
                new[] { "UNKNOWN" },
                new[] { "" },
                new[] { "KUB", "kub" },
                new[] { "KUB", "C001", "C002", "C003" },
                new[] { "KUB_THB" },
            }
        )
        {
            var rejected = false;
            try
            {
                await reader.Read(CancellationToken.None, invalid);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, "Invalid or unknown research candidate was accepted.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
