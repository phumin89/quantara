using System.Reflection;
using System.Text.Json;
using Quantara.MarketData.Domain;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketFeedIdentityChecks
{
    private const string depth = """{"event":"depthchanged","data":{"bids":[[99,2]],"asks":[[101,3]]}}""";

    public static async Task Run()
    {
        var previous = new MarketWatchSymbol("BTC_THB", 42, 1000);
        var replacement = previous with { PairingId = 43 };
        var ether = new MarketWatchSymbol("ETH_THB", 7, 100);
        IReadOnlyList<MarketCandle> candles = [new(3000, 100, 101, 99, 100, 10)];
        IReadOnlyList<MarketCandle> lateCandles = [new(3300, 200, 201, 199, 200, 20)];
        IReadOnlyList<MarketCandle> currentCandles = [new(3300, 110, 111, 109, 110, 20)];
        using var handler = new UniverseHttpHandler
        {
            SymbolsResponse = Rules([previous, ether]),
            TickerResponse =
                """[{"symbol":"BTC_THB","last":100,"highest_bid":99,"lowest_ask":101,"quote_volume":1000,"high_24_hr":101,"low_24_hr":99,"percent_change":1},{"symbol":"ETH_THB","last":200,"highest_bid":199,"lowest_ask":201,"quote_volume":100,"high_24_hr":201,"low_24_hr":199,"percent_change":1}]""",
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid") };
        var time = new AdvancingMarketTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3600));
        using var feed = new LiveMarketFeed(client, new UnusedIntradayCandleStore(), time);
        await Refresh(feed);
        SetTickerConnection(feed, previous, true);
        SetTickerConnection(feed, ether, true);
        ParseTicker(feed, previous, Ticker(previous, 100));
        ParseTicker(feed, ether, Ticker(ether, 200));
        SetSeries(feed, previous, candles);
        SetSeries(feed, ether, candles);
        SetDepth(feed, previous, depth);
        Check(Quote(feed, previous).BookEventJson == depth, "fixture has a complete current book");
        var unchangedEther = Quote(feed, ether);
        var unchangedEtherSeries = feed.Read().Series.Single(series => series.Symbol == ether.Symbol);
        ParseTicker(feed, previous, Ticker(ether, 300));
        Check(
            Quote(feed, ether) == unchangedEther,
            "a ticker frame must match its own subscription even when its payload symbol is also watched"
        );

        handler.SymbolsResponse = Rules([replacement, ether]);
        time.Advance(TimeSpan.FromMinutes(1));
        await Refresh(feed);
        Check(
            !feed.Read().Quotes.Any(quote => quote.Symbol == previous.Symbol),
            "pairing replacement invalidates the retired ticker quote before the replacement connects"
        );
        ParseTicker(feed, previous, Ticker(previous, 150));
        SetSeries(feed, previous, lateCandles);
        Check(
            !feed.Read().Quotes.Any(quote => quote.Symbol == previous.Symbol)
                && feed.Read().Series.Single(series => series.Symbol == previous.Symbol).Candles.SequenceEqual(candles),
            "retired pairing frames and late candle completions cannot update the replacement"
        );

        SetTickerConnection(feed, replacement, true);
        ParseTicker(feed, replacement, Ticker(replacement, 110));
        var cacheCleared = Quote(feed, replacement).BookEventJson is null;
        SetSeries(feed, replacement, currentCandles);
        Check(
            Quote(feed, replacement).Last == 110
                && feed.Read()
                    .Series.Single(series => series.Symbol == replacement.Symbol)
                    .Candles.SequenceEqual(currentCandles),
            "the current pairing accepts its ticker and candle evidence"
        );
        SetDepth(feed, previous, depth);
        var lateFrameRejected = Quote(feed, replacement).BookEventJson is null;
        SetDepth(feed, replacement, depth);
        Check(Quote(feed, replacement).BookEventJson == depth, "new pairing can publish fresh depth");
        SetDepth(feed, previous, null);
        var lateDisconnectRejected = Quote(feed, replacement).BookEventJson == depth;
        var replacementQuote = Quote(feed, replacement);
        SetTickerConnection(feed, previous, false);
        Check(
            Quote(feed, replacement) == replacementQuote && Quote(feed, ether) == unchangedEther,
            "a retired ticker disconnect cannot clear the replacement or another symbol"
        );

        handler.SymbolsResponse = Rules([replacement, ether], true);
        time.Advance(TimeSpan.FromMinutes(1));
        await Refresh(feed);
        var ruleChangeCleared = Quote(feed, replacement).BookEventJson is null;
        SetDepth(feed, replacement, depth);
        Check(
            Quote(feed, replacement).BookEventJson == depth,
            "unchanged identity can provide fresh depth after a rule update"
        );

        handler.SymbolsResponse = Rules([ether]);
        time.Advance(TimeSpan.FromMinutes(1));
        await Refresh(feed);
        ParseTicker(feed, replacement, Ticker(replacement, 120));
        SetTickerConnection(feed, replacement, false);
        SetSeries(feed, replacement, lateCandles);
        SetDepth(feed, replacement, depth);
        Check(
            !feed.Read().Quotes.Any(quote => quote.Symbol == replacement.Symbol)
                && !feed.Read().Series.Any(series => series.Symbol == replacement.Symbol)
                && Quote(feed, ether) == unchangedEther
                && feed.Read().Series.Single() == unchangedEtherSeries
                && feed.Read().Connected,
            "late removed-market work cannot restore quotes or candles, or disturb the retained symbol"
        );

        handler.SymbolsResponse = Rules([replacement, ether], true);
        time.Advance(TimeSpan.FromMinutes(1));
        await Refresh(feed);
        SetTickerConnection(feed, replacement, true);
        ParseTicker(feed, replacement, Ticker(replacement, 130));
        SetSeries(feed, replacement, currentCandles);
        var removedFrameRejected = Quote(feed, replacement).BookEventJson is null;
        Check(
            feed.Read()
                .Series.Single(series => series.Symbol == replacement.Symbol)
                .Candles.SequenceEqual(currentCandles),
            "a re-added current pairing accepts fresh candle evidence"
        );
        Check(
            cacheCleared && lateFrameRejected && lateDisconnectRejected && ruleChangeCleared && removedFrameRejected,
            $"metadata/session boundary: cache cleared={cacheCleared}, old frame rejected={lateFrameRejected}, old disconnect rejected={lateDisconnectRejected}, rule cache cleared={ruleChangeCleared}, removed frame rejected={removedFrameRejected}"
        );
        Check(Quote(feed, ether) == unchangedEther, "other symbols retain their quote evidence");
        Console.WriteLine(
            "Market subscription, candle completion and retired identity checks passed without sockets or storage."
        );
    }

    private static string Rules(IReadOnlyList<MarketWatchSymbol> symbols, bool freezeBuy = false)
    {
        return JsonSerializer.Serialize(
            new
            {
                result = symbols.Select(symbol => new
                {
                    base_asset = symbol.Symbol.Split('_')[0],
                    quote_asset = "THB",
                    status = "active",
                    market_segment = "SPOT",
                    source = "exchange",
                    pairing_id = symbol.PairingId,
                    price_step = 0.1m,
                    price_scale = 1,
                    base_asset_scale = 6,
                    quote_asset_scale = 2,
                    min_quote_size = 10,
                    freeze_buy = freezeBuy && symbol.Symbol == "BTC_THB",
                    freeze_sell = false,
                }),
            }
        );
    }

    private static string Ticker(MarketWatchSymbol symbol, decimal price) =>
        JsonSerializer.Serialize(
            new
            {
                stream = "market.ticker.thb_" + symbol.Symbol.Split('_')[0].ToLowerInvariant(),
                highestBid = price - 1,
                lowestAsk = price + 1,
                last = price,
                isFrozen = 0,
            }
        );

    private static LiveMarketQuote Quote(LiveMarketFeed feed, MarketWatchSymbol symbol) =>
        feed.Read().Quotes.Single(quote => quote.Symbol == symbol.Symbol);

    private static async Task Refresh(LiveMarketFeed feed)
    {
        var pending =
            Invoke(feed, "RefreshWatch", CancellationToken.None) as Task
            ?? throw new InvalidOperationException("Watch refresh did not return a task.");
        await pending;
    }

    private static void ParseTicker(LiveMarketFeed feed, MarketWatchSymbol source, string json)
    {
        Invoke(feed, "ParseMessage", source, json);
    }

    private static void SetTickerConnection(LiveMarketFeed feed, MarketWatchSymbol symbol, bool connected)
    {
        Invoke(feed, "SetTickerConnection", symbol, connected);
    }

    private static void SetSeries(LiveMarketFeed feed, MarketWatchSymbol symbol, IReadOnlyList<MarketCandle> candles)
    {
        Invoke(feed, "SetSeries", symbol, candles, null);
    }

    private static void SetDepth(LiveMarketFeed feed, MarketWatchSymbol symbol, string? json)
    {
        Invoke(feed, "SetDepth", symbol, json);
    }

    private static object? Invoke(LiveMarketFeed feed, string name, params object?[] arguments)
    {
        var method =
            typeof(LiveMarketFeed).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Feed method missing: " + name);
        return method.Invoke(feed, arguments);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Market identity check failed: " + message);
        }
    }
}
