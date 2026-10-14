using System.Text.Json;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class ResearchEvidenceChecks
{
    public static async Task Run()
    {
        var symbols = Enumerable
            .Range(1, 247)
            .Select(index => new
            {
                base_asset = index == 1 ? "BTC" : "C" + index,
                quote_asset = "THB",
                status = "active",
                market_segment = "SPOT",
                source = "exchange",
                pairing_id = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToArray();
        var quotes = symbols
            .Select(
                (item, index) =>
                    new
                    {
                        symbol = item.base_asset + "_THB",
                        last = 100 + index,
                        highest_bid = 99 + index,
                        lowest_ask = 101 + index,
                        quote_volume = 1000 - index,
                        high_24_hr = 110 + index,
                        low_24_hr = 90 + index,
                        percent_change = index,
                    }
            )
            .ToArray();
        using var symbolDocument = JsonDocument.Parse(JsonSerializer.Serialize(new { result = symbols }));
        using var tickerDocument = JsonDocument.Parse(JsonSerializer.Serialize(quotes));
        var scan = MarketUniverseScanner.Scan(symbolDocument.RootElement, tickerDocument.RootElement);
        if (scan.Entries.Count != 247 || scan.Entries.Any(entry => !entry.Usable))
        {
            throw new InvalidOperationException("The large-universe fixture must contain 247 usable markets.");
        }

        var shortlist = MarketUniverseScanner.SelectCandidates(scan, 16);
        var signalSymbol = scan.Entries.First(entry => !shortlist.Any(item => item.Symbol == entry.Symbol)).Symbol;
        var monitored = shortlist.Take(15).Select(entry => entry.Symbol).Append(signalSymbol).ToArray();
        var now = DateTimeOffset.UtcNow;
        var lastTimestamp = now.ToUnixTimeSeconds() / 300 * 300 - 300;
        var candles = Enumerable
            .Range(1, 21)
            .Select(index => new MarketCandle(
                lastTimestamp - (21 - index) * 300,
                1.000000000000000000m,
                3.000000000000000000m,
                0.999999999999999999m,
                index == 21 ? 2.0000000000000000000000000001m : 1.500000000000000000m,
                index == 21 ? 0.0000000000000000000000000001m : 10.000000000000000000m
            ))
            .ToArray();
        var snapshot = new IntradaySnapshot(
            true,
            now,
            true,
            monitored.Select(symbol => new LiveMarketQuote(symbol, 1m, 2m, 1.5m, now, null)).ToArray(),
            monitored.Select(symbol => new IntradaySeries(symbol, candles, now, null)).ToArray(),
            [
                new MarketSignal(
                    "signal-1",
                    signalSymbol,
                    SignalDirection.Breakout,
                    lastTimestamp,
                    now,
                    candles[^1].Close,
                    1m,
                    2m,
                    0.1m,
                    "test"
                ),
            ],
            null
        );
        var dailyEnd = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var history = JsonSerializer.Serialize(
            new
            {
                s = "ok",
                t = Enumerable.Range(0, 110).Select(index => dailyEnd - (110 - index) * 86400),
                o = Enumerable.Repeat(1.000000000000000000m, 110),
                h = Enumerable.Repeat(3.000000000000000000m, 110),
                l = Enumerable.Repeat(0.999999999999999999m, 110),
                c = Enumerable.Repeat(2.0000000000000000000000000001m, 110),
                v = Enumerable.Repeat(0.0000000000000000000000000001m, 110),
            }
        );
        var handler = new ResearchHttpHandler
        {
            SymbolsResponse = symbolDocument.RootElement.GetRawText(),
            TickerResponse = tickerDocument.RootElement.GetRawText(),
            HistoryResponse = history,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var payload = await new BitkubResearchMarketReader(client, new ResearchIntradayReader(snapshot)).Read(
            CancellationToken.None
        );
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var intraday = root.GetProperty("intraday");
        var series = intraday.GetProperty("series");
        if (
            root.GetProperty("scan").GetProperty("symbols").GetArrayLength() != 247
            || root.GetProperty("markets").GetArrayLength() != 16
        )
        {
            throw new InvalidOperationException(
                "All active universe rows must remain scanned while details stay bounded."
            );
        }

        if (
            series[0].GetProperty("symbol").GetString() != signalSymbol
            || !root.GetProperty("markets")
                .EnumerateArray()
                .Any(item => item.GetProperty("symbol").GetString() == signalSymbol)
        )
        {
            throw new InvalidOperationException(
                "A signal outside the shortlist must receive intraday market evidence."
            );
        }

        var signalSeries = series
            .EnumerateArray()
            .Single(item => item.GetProperty("symbol").GetString() == signalSymbol);
        if (
            signalSeries.GetProperty("candles").GetArrayLength() != 21
            || signalSeries.GetProperty("candles")[20][4].GetDecimal() != candles[^1].Close
            || signalSeries.GetProperty("candles")[20][5].GetDecimal() != candles[^1].Volume
            || !intraday.GetProperty("signals")[0].GetProperty("evidenceAvailable").GetBoolean()
        )
        {
            throw new InvalidOperationException("Complete signal candles must be retained exactly.");
        }

        if (
            intraday.GetProperty("candleColumns").GetArrayLength() != 6
            || root.GetProperty("historicalCandles").GetProperty("candleColumns").GetArrayLength() != 6
            || root.GetProperty("historicalCandles").GetProperty("candles")[20][4].GetDecimal() != candles[^1].Close
            || root.GetProperty("historicalCandles").GetProperty("candles")[20][5].GetDecimal() != candles[^1].Volume
        )
        {
            throw new InvalidOperationException("Intraday and daily history must use compact column metadata.");
        }

        if (
            payload.Length > 32000
            || payload.Contains("1.000000000000000000", StringComparison.Ordinal)
            || payload.Contains("10.000000000000000000", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "Research JSON must remain bounded without repeated decimal zero padding."
            );
        }

        if (
            !intraday
                .GetProperty("signals")
                .EnumerateArray()
                .Any(item => item.GetProperty("symbol").GetString() == signalSymbol)
        )
        {
            throw new InvalidOperationException("Signal evidence must include the promoted signal market.");
        }

        foreach (
            var invalidCandles in new[]
            {
                candles.Take(20).ToArray(),
                candles
                    .Select(
                        (candle, index) => index == 10 ? candle with { Timestamp = candle.Timestamp - 300 } : candle
                    )
                    .ToArray(),
                Array.Empty<MarketCandle>(),
            }
        )
        {
            var incomplete = snapshot with
            {
                Series = snapshot
                    .Series.Select(item => item.Symbol == signalSymbol ? item with { Candles = invalidCandles } : item)
                    .ToArray(),
            };
            using var incompleteClient = new HttpClient(
                new ResearchHttpHandler
                {
                    SymbolsResponse = symbolDocument.RootElement.GetRawText(),
                    TickerResponse = tickerDocument.RootElement.GetRawText(),
                }
            )
            {
                BaseAddress = new Uri("https://test.invalid"),
            };
            using var incompleteDocument = JsonDocument.Parse(
                await new BitkubResearchMarketReader(incompleteClient, new ResearchIntradayReader(incomplete)).Read(
                    CancellationToken.None
                )
            );
            var incompleteSignal = incompleteDocument
                .RootElement.GetProperty("intraday")
                .GetProperty("signals")
                .EnumerateArray()
                .Single(item => item.GetProperty("symbol").GetString() == signalSymbol);
            if (incompleteSignal.GetProperty("evidenceAvailable").GetBoolean())
            {
                throw new InvalidOperationException(
                    "Signals with short, gapped or missing candle proof must report unavailable evidence."
                );
            }
        }
    }
}
