using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Infrastructure;

public sealed class BitkubResearchMarketReader : IResearchMarketReader
{
    private readonly HttpClient client;

    public BitkubResearchMarketReader(HttpClient client)
    {
        this.client = client;
    }

    public async Task<string> Read(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var symbols = await this.Get("/api/v3/market/symbols", timeout.Token);
        var ticker = await this.Get("/api/v3/market/ticker", timeout.Token);
        EnsureSuccess(symbols);
        EnsureSuccess(ticker);
        var symbolRules = symbols
            .GetProperty("result")
            .EnumerateArray()
            .Where(x =>
                Text(x, "quote_asset") == "THB"
                && Text(x, "status") == "active"
                && Text(x, "market_segment") == "SPOT"
                && Text(x, "source") == "exchange"
            )
            .GroupBy(x => Text(x, "base_asset").ToUpperInvariant())
            .ToDictionary(x => x.Key, x => x.First());
        var quotes = ticker
            .EnumerateArray()
            .Where(x =>
                Text(x, "symbol").EndsWith("_THB", StringComparison.Ordinal)
                && symbolRules.ContainsKey(Text(x, "symbol").Split('_')[0])
            )
            .OrderByDescending(x => RequiredNumber(x, "quote_volume"))
            .Take(20)
            .ToList();
        if (!quotes.Any(x => Text(x, "symbol") == "BTC_THB") && symbolRules.ContainsKey("BTC"))
        {
            var btc = ticker.EnumerateArray().FirstOrDefault(x => Text(x, "symbol") == "BTC_THB");
            if (btc.ValueKind != JsonValueKind.Undefined)
            {
                if (quotes.Count == 20)
                {
                    quotes.RemoveAt(19);
                }

                quotes.Add(btc);
            }
        }

        var markets = new List<object>();
        foreach (var quote in quotes)
        {
            var symbol = Text(quote, "symbol");
            var rule = symbolRules[symbol.Split('_')[0]];
            var item = new Dictionary<string, object?>
            {
                ["symbol"] = symbol,
                ["last"] = RequiredNumber(quote, "last"),
                ["bid"] = RequiredNumber(quote, "highest_bid"),
                ["ask"] = RequiredNumber(quote, "lowest_ask"),
                ["high24h"] = OptionalNumber(quote, "high_24_hr"),
                ["low24h"] = OptionalNumber(quote, "low_24_hr"),
                ["change24h"] = OptionalNumber(quote, "percent_change"),
                ["volume24h"] = RequiredNumber(quote, "quote_volume"),
                ["rules"] = new
                {
                    minNotional = OptionalNumber(rule, "min_quote_size"),
                    priceStep = OptionalNumber(rule, "price_step"),
                    priceScale = OptionalNumber(rule, "price_scale"),
                    baseAssetScale = OptionalNumber(rule, "base_asset_scale"),
                    quoteAssetScale = OptionalNumber(rule, "quote_asset_scale"),
                    freezeBuy = OptionalBool(rule, "freeze_buy"),
                    freezeSell = OptionalBool(rule, "freeze_sell"),
                },
            };
            if (markets.Count < 5)
            {
                item["depth"] = await this.GetOptional(
                    $"/api/v3/market/depth?sym={Uri.EscapeDataString(symbol)}&lmt=5",
                    timeout.Token
                );
                item["trades"] = await this.GetOptional(
                    $"/api/v3/market/trades?sym={Uri.EscapeDataString(symbol)}&lmt=10",
                    timeout.Token
                );
            }

            markets.Add(item);
        }

        var result = JsonSerializer.Serialize(
            new
            {
                available = true,
                currency = "THB",
                fetchedAt = DateTimeOffset.UtcNow,
                coverage = new
                {
                    universeCount = symbolRules.Count,
                    truncated = symbolRules.Count > 20,
                    topDepthCount = Math.Min(5, markets.Count),
                },
                markets,
                historicalCandles = await this.History(cancellationToken),
                tradeEvidence = "Recent executed trades are flow evidence, not OHLC backtest history.",
            }
        );
        return result.Length <= 40000
            ? result
            : throw new InvalidDataException("Market research snapshot is too large.");
    }

    private async Task<JsonElement> Get(string path, CancellationToken token)
    {
        using var r = await this.client.GetAsync(path, token);
        r.EnsureSuccessStatusCode();
        using var d = JsonDocument.Parse(await r.Content.ReadAsStreamAsync(token));
        EnsureSuccess(d.RootElement);
        return d.RootElement.Clone();
    }

    private async Task<object> History(CancellationToken cancellationToken)
    {
        var to = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var from = to - 110 * 86400;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var raw = await this.Get(
                $"/tradingview/history?symbol=BTC_THB&resolution=1D&from={from}&to={to}",
                timeout.Token
            );
            var candles = CandleParser.Parse(raw, from, to);
            return new
            {
                available = candles.Count > 0,
                symbol = "BTC_THB",
                resolution = "1D",
                from,
                to,
                expectedBars = 110,
                returnedBars = candles.Count,
                excludedRows = raw.GetProperty("t").GetArrayLength() - candles.Count,
                complete = candles.Count == 110,
                candles,
                source = "Bitkub public TradingView history compatibility endpoint; completed daily bars only. Conflicting timestamps excluded; gaps are not filled. This dataset is not a backtest result.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new
            {
                available = false,
                symbol = "BTC_THB",
                reason = "Historical candles could not be verified.",
            };
        }
    }

    private async Task<JsonElement?> GetOptional(string path, CancellationToken token)
    {
        try
        {
            return await this.Get(path, token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Text(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.ToString() : "";

    private static decimal RequiredNumber(JsonElement e, string p) =>
        decimal.TryParse(Text(e, p), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new InvalidDataException($"Missing {p}.");

    private static decimal? OptionalNumber(JsonElement e, string p) =>
        decimal.TryParse(Text(e, p), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static bool? OptionalBool(JsonElement e, string p) => bool.TryParse(Text(e, p), out var b) ? b : null;

    private static void EnsureSuccess(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) && e.ToString() != "0")
        {
            throw new InvalidDataException("Bitkub error.");
        }
    }
}
