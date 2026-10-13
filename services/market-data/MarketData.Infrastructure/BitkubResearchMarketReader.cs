using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Infrastructure;

public sealed class BitkubResearchMarketReader : IResearchMarketReader
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] candleColumns = ["timestamp", "open", "high", "low", "close", "volume"];

    private readonly HttpClient client;

    private readonly IIntradayMarketReader? intraday;

    public BitkubResearchMarketReader(HttpClient client, IIntradayMarketReader? intraday = null)
    {
        this.client = client;
        this.intraday = intraday;
    }

    public async Task<string> Read(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var symbols = await this.Get("/api/v3/market/symbols", timeout.Token);
        var ticker = await this.Get("/api/v3/market/ticker", timeout.Token);
        EnsureSuccess(symbols);
        EnsureSuccess(ticker);
        var scan = MarketUniverseScanner.Scan(symbols, ticker);
        var candidates = MarketUniverseScanner.SelectCandidates(scan, 16);

        var markets = new List<object>();
        foreach (var entry in candidates)
        {
            var symbol = entry.Symbol;
            if (!entry.SourceQuote.HasValue)
            {
                throw new InvalidDataException("Usable market candidate has no source quote.");
            }

            var quote = entry.SourceQuote.Value;
            var rule = entry.SourceRule;
            var item = new Dictionary<string, object?>
            {
                ["symbol"] = symbol,
                ["last"] = entry.Last,
                ["bid"] = RequiredNumber(quote, "highest_bid"),
                ["ask"] = RequiredNumber(quote, "lowest_ask"),
                ["high24h"] = OptionalNumber(quote, "high_24_hr"),
                ["low24h"] = OptionalNumber(quote, "low_24_hr"),
                ["change24h"] = entry.Change24h,
                ["range24h"] = RoundScreening(entry.Range24h, 4),
                ["volume24h"] = entry.Volume24h,
                ["spreadPercent"] = RoundScreening(entry.SpreadPercent, 4),
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

        var history = await this.History(cancellationToken);
        foreach (var seriesLimit in new[] { 8, 4, 2, 0 })
        {
            var result = JsonSerializer.Serialize(
                new
                {
                    available = true,
                    currency = "THB",
                    fetchedAt = DateTimeOffset.UtcNow,
                    coverage = new
                    {
                        universeCount = scan.UniverseCount,
                        scannedCount = scan.Entries.Count,
                        missingQuoteCount = scan.MissingQuoteCount,
                        excludedCount = scan.ExcludedCount,
                        duplicateConflictCount = scan.DuplicateConflictCount,
                        truncated = false,
                        detailedMarketCount = markets.Count,
                        detailedMarketsTruncated = markets.Count < scan.Entries.Count,
                        selection = "Diverse liquidity and absolute 24h movement shortlist including usable BTC; screening heuristic, not expected profit or validated edge.",
                        topDepthCount = Math.Min(5, markets.Count),
                    },
                    scan = new
                    {
                        precision = "Screening metrics are rounded (change/range/spread: 4 decimals, THB volume: 2). Detailed market quotes/rules retain source precision for proposals.",
                        symbols = scan.Entries.Select(entry => entry.Symbol),
                        last = scan.Entries.Select(entry => entry.Last),
                        change24hPercent = scan.Entries.Select(entry => RoundScreening(entry.Change24h, 4)),
                        range24hPercent = scan.Entries.Select(entry => RoundScreening(entry.Range24h, 4)),
                        quoteVolume24hThb = scan.Entries.Select(entry => RoundScreening(entry.Volume24h, 2)),
                        spreadPercent = scan.Entries.Select(entry => RoundScreening(entry.SpreadPercent, 4)),
                        usable = scan.Entries.Select(entry => entry.Usable),
                    },
                    markets,
                    historicalCandles = history,
                    intraday = this.ReadIntraday(
                        candidates.Take(seriesLimit).Select(entry => entry.Symbol).ToHashSet()
                    ),
                    tradeEvidence = "Recent executed trades are flow evidence, not OHLC backtest history.",
                },
                BitkubResearchMarketReader.jsonOptions
            );
            if (result.Length <= 32000)
            {
                return result;
            }
        }

        throw new InvalidDataException("Market research snapshot is too large.");
    }

    private object ReadIntraday(HashSet<string> prioritySymbols)
    {
        var snapshot = this.intraday?.Read();
        if (snapshot is null)
        {
            return new { available = false, reason = "Intraday feed is unavailable." };
        }

        return new
        {
            snapshot.Available,
            snapshot.CheckedAt,
            snapshot.Connected,
            snapshot.Error,
            timeframeSeconds = 300,
            coverage = "Up to 16 diverse THB spot markets monitored; last 21 retained completed candles shown for up to 8 priority candidates. Missing candles are not filled.",
            monitoredSeriesCount = snapshot.Series.Count,
            omittedSeriesCount = snapshot.Series.Count(series => !prioritySymbols.Contains(series.Symbol)),
            source = "Bitkub public WebSocket ticker (ObservedAt is receive time) and TradingView 5-minute history compatibility endpoint. Order book updates are events, not reconstructed full books.",
            candleColumns = BitkubResearchMarketReader.candleColumns,
            quotes = snapshot.Quotes.Select(quote => new
            {
                quote.Symbol,
                quote.Bid,
                quote.Ask,
                quote.Last,
                quote.ObservedAt,
                quote.BookObservedAt,
                bookEventAvailable = quote.BookEventJson is not null,
            }),
            series = snapshot
                .Series.Where(series => prioritySymbols.Contains(series.Symbol))
                .Select(series => new
                {
                    series.Symbol,
                    series.FetchedAt,
                    series.Error,
                    returnedBars = series.Candles.Count,
                    truncated = series.Candles.Count > 21,
                    candles = series
                        .Candles.TakeLast(21)
                        .Select(candle =>
                            new decimal[]
                            {
                                candle.Timestamp,
                                candle.Open,
                                candle.High,
                                candle.Low,
                                candle.Close,
                                candle.Volume,
                            }
                        ),
                }),
            signals = snapshot.Signals.Select(signal => new
            {
                signal.Id,
                signal.Symbol,
                direction = signal.Direction.ToString().ToLowerInvariant(),
                signal.CandleAt,
                signal.DetectedAt,
                signal.Price,
                signal.PriorLevel,
                signal.VolumeRatio,
                signal.SpreadPercent,
                signal.RuleVersion,
            }),
            rule = "Research alert only: closed 5-minute close outside previous 20-bar high/low, volume at least 1.5x previous 20-bar median, current spread at most 0.5%, fresh quote and consecutive verified candles. Thresholds are unvalidated heuristics; no backtest or net-profit proof. Never an order or approval.",
        };
    }

    private static decimal? RoundScreening(decimal? value, int places) =>
        value is decimal number ? decimal.Round(number, places) : null;

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
            var retainedCandles = candles.TakeLast(21).ToArray();
            return new
            {
                available = candles.Count > 0,
                symbol = "BTC_THB",
                resolution = "1D",
                from,
                to,
                expectedBars = 110,
                returnedBars = retainedCandles.Length,
                sourceReturnedBars = candles.Count,
                excludedRows = raw.GetProperty("t").GetArrayLength() - candles.Count,
                truncated = candles.Count > retainedCandles.Length,
                complete = candles.Count == 110,
                candles = retainedCandles,
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
