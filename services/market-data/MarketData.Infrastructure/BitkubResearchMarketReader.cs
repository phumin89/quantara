using System.Globalization;
using System.Text.Json;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

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

    public async Task<string> Read(CancellationToken cancellationToken, IReadOnlyList<string>? symbols = null)
    {
        var requestedSymbols = NormalizeRequestedSymbols(symbols);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var symbolDocument = await this.Get("/api/v3/market/symbols", timeout.Token);
        var ticker = await this.Get("/api/v3/market/ticker", timeout.Token);
        EnsureSuccess(symbolDocument);
        EnsureSuccess(ticker);
        var scan = MarketUniverseScanner.Scan(symbolDocument, ticker);
        var requestedEntries = requestedSymbols
            .Select(symbol => scan.Entries.SingleOrDefault(entry => entry.Symbol == symbol + "_THB"))
            .ToArray();
        if (requestedEntries.Any(entry => entry is null || !entry.Usable))
        {
            throw new ArgumentException("Requested symbols must be active THB spot markets with usable quotes.");
        }

        var snapshot = this.intraday?.Read();
        var monitoredSeriesCount = snapshot?.Series.Count ?? 0;
        var assessedAt = DateTimeOffset.UtcNow;
        if (requestedSymbols.Length > 0)
        {
            var requestedSeries = await this.FetchRequestedSeries(requestedSymbols, timeout.Token);
            var requestedEvidenceAvailable = requestedSeries.Any(series =>
                series.Error is null && series.Candles.Count > 0
            );
            snapshot = (snapshot ?? new IntradaySnapshot(false, assessedAt, false, [], [], [], null)) with
            {
                Available = snapshot?.Available == true || requestedEvidenceAvailable,
                Series = (snapshot?.Series ?? [])
                    .Where(series => !requestedSeries.Any(requested => requested.Symbol == series.Symbol))
                    .Concat(requestedSeries)
                    .ToArray(),
                Error = snapshot?.Error,
            };
        }

        assessedAt = DateTimeOffset.UtcNow;
        var alertSymbols = snapshot?.Signals.Select(signal => signal.Symbol).ToHashSet(StringComparer.Ordinal) ?? [];
        var validRequestedEntries = requestedEntries
            .Select(entry =>
                entry
                ?? throw new ArgumentException("Requested symbols must be active THB spot markets with usable quotes.")
            )
            .ToArray();
        var candidates = validRequestedEntries
            .Concat(
                scan.Entries.Where(entry => entry.Usable && alertSymbols.Contains(entry.Symbol))
                    .OrderBy(entry => entry.Symbol)
                    .Concat(MarketUniverseScanner.SelectCandidates(scan, Math.Max(0, 16 - requestedEntries.Length)))
            )
            .DistinctBy(entry => entry.Symbol)
            .Take(16)
            .ToArray();
        if (requestedSymbols.Length > 0)
        {
            candidates = validRequestedEntries;
        }

        var priorities = requestedSymbols
            .Select(symbol => symbol + "_THB")
            .Concat(PrioritizeSeries(candidates, snapshot))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedSymbols.Length > 0)
        {
            priorities = requestedSymbols.Select(symbol => symbol + "_THB").ToArray();
        }

        var priorityOrder = priorities
            .Select((symbol, index) => new { symbol, index })
            .ToDictionary(item => item.symbol, item => item.index);
        if (priorities.Length > 0)
        {
            candidates = candidates
                .OrderBy(entry => priorityOrder.GetValueOrDefault(entry.Symbol, 100))
                .ThenBy(entry => entry.Symbol)
                .ToArray();
        }

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

        object history =
            requestedSymbols.Length == 0
                ? await this.History(cancellationToken)
                : new
                {
                    available = false,
                    reason = "Requested intraday evidence is prioritized; default daily BTC history was omitted.",
                };

        foreach (var seriesLimit in Enumerable.Range(0, 9).Reverse())
        {
            if (seriesLimit < requestedSymbols.Length)
            {
                break;
            }

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
                        selection = requestedSymbols.Length > 0
                            ? "Explicit research candidates; the complete compact universe scan is retained."
                            : "Diverse liquidity and absolute 24h movement shortlist including usable BTC; screening heuristic, not expected profit or validated edge.",
                        topDepthCount = Math.Min(5, markets.Count),
                        candleSelection = requestedSymbols.Length > 0
                            ? "Explicit requested candidates in request order. Every requested series is retained or the request fails its budget; no new live subscription is created."
                            : "Alerts first, then BTC and diverse liquid/moving pairs with supplied bars. Continuity is stated per series. Routing only; movement/spread ranking is not expected profit or an entry rule.",
                    },
                    scan = new
                    {
                        precision = "Screening metrics are rounded (change/range/spread: 4 decimals, THB volume: 2). Detailed market quotes/rules retain source precision for proposals.",
                        symbols = scan.Entries.Select(entry => entry.Symbol),
                        last = scan.Entries.Select(entry =>
                            entry.Last is decimal last ? Normalize(last) : (decimal?)null
                        ),
                        change24hPercent = scan.Entries.Select(entry => RoundScreening(entry.Change24h, 4)),
                        range24hPercent = scan.Entries.Select(entry => RoundScreening(entry.Range24h, 4)),
                        quoteVolume24hThb = scan.Entries.Select(entry => RoundScreening(entry.Volume24h, 2)),
                        spreadPercent = scan.Entries.Select(entry => RoundScreening(entry.SpreadPercent, 4)),
                        usable = scan.Entries.Select(entry => entry.Usable),
                    },
                    markets,
                    historicalCandles = history,
                    intraday = ReadIntraday(
                        snapshot,
                        priorities.Take(seriesLimit).ToArray(),
                        assessedAt,
                        monitoredSeriesCount
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

    private static string[] NormalizeRequestedSymbols(IReadOnlyList<string>? symbols)
    {
        if (symbols is null || symbols.Count == 0)
        {
            return [];
        }

        if (
            symbols.Count > 3
            || symbols.Any(symbol =>
                symbol.Length is < 1 or > 30 || symbol.Any(character => !char.IsAsciiLetterOrDigit(character))
            )
        )
        {
            throw new ArgumentException("At most three ASCII alphanumeric symbols are allowed.");
        }

        var normalized = symbols.Select(symbol => symbol.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length != symbols.Count)
        {
            throw new ArgumentException("Requested symbols must be unique.");
        }

        return normalized;
    }

    private async Task<IReadOnlyList<IntradaySeries>> FetchRequestedSeries(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        return await Task.WhenAll(
            symbols.Select(symbol => this.FetchRequestedSeries(symbol, budget.Token, cancellationToken))
        );
    }

    private async Task<IntradaySeries> FetchRequestedSeries(
        string symbol,
        CancellationToken cancellationToken,
        CancellationToken requestToken
    )
    {
        var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300 * 300;
        var from = to - 120 * 300;
        try
        {
            var raw = await this.Get(
                $"/tradingview/history?symbol={Uri.EscapeDataString(symbol + "_THB")}&resolution=5&from={from}&to={to}",
                cancellationToken
            );
            // The endpoint can include the unfinished bar at the inclusive upper boundary.
            var candles = CandleParser.Parse(raw, from, to, 300, 121).TakeLast(120).ToArray();
            return new IntradaySeries(
                symbol + "_THB",
                candles,
                DateTimeOffset.UtcNow,
                candles.Length == 0 ? "No completed candles returned." : null
            );
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !requestToken.IsCancellationRequested)
        {
            return new IntradaySeries(
                symbol + "_THB",
                [],
                DateTimeOffset.UtcNow,
                "Requested candle evidence could not be verified."
            );
        }
    }

    private static object ReadIntraday(
        IntradaySnapshot? snapshot,
        string[] priorities,
        DateTimeOffset assessedAt,
        int monitoredSeriesCount
    )
    {
        if (snapshot is null)
        {
            return new { available = false, reason = "Intraday feed is unavailable." };
        }

        var prioritySymbols = priorities.ToHashSet(StringComparer.Ordinal);

        return new
        {
            snapshot.Available,
            snapshot.CheckedAt,
            snapshot.Connected,
            snapshot.Error,
            timeframeSeconds = 300,
            coverage = "Up to 16 diverse THB spot markets monitored; last 21 retained completed candles shown for up to 8 priority candidates. Missing candles are not filled.",
            monitoredSeriesCount,
            omittedSeriesCount = snapshot.Series.Count(series => !prioritySymbols.Contains(series.Symbol)),
            omittedQuoteCount = snapshot.Quotes.Count(quote => !prioritySymbols.Contains(quote.Symbol)),
            source = "Bitkub public WebSocket ticker (ObservedAt is receive time); requested symbols may include separately fetched completed 5-minute TradingView history outside the monitored set. A request is not a live subscription. Order book updates are events, not reconstructed full books.",
            candleColumns = BitkubResearchMarketReader.candleColumns,
            numericEncoding = "Lossless decimal values; insignificant trailing zeros are omitted.",
            adaptiveCheckedAt = assessedAt,
            adaptiveRule = "adaptive-ema-atr-v1: EMA20/50 SMA seeds; ATR14 Wilder seed from 14 true ranges with previous closes. At least 70 consecutive completed bars, at most 120. Last 3 bar regimes must agree: gap=(EMA20-EMA50)/ATR >=0.5 with positive 5-bar EMA20 slope is uptrend; <=-0.5 with negative slope is downtrend; absolute gap and slope/ATR <=0.25 is range; otherwise transition. ATR/previous20 ATR median >=1.5 high, <=0.75 low; zero baseline ratio is unknown. Research routing heuristics, not profit proof or trade gates.",
            quotes = snapshot
                .Quotes.Where(quote => prioritySymbols.Contains(quote.Symbol))
                .Select(quote => new
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
                .OrderBy(series => Array.IndexOf(priorities, series.Symbol))
                .Select(series => new
                {
                    series.Symbol,
                    series.FetchedAt,
                    series.Error,
                    returnedBars = series.Candles.Count,
                    truncated = series.Candles.Count > 21,
                    consecutiveBars = CanVerify(series),
                    adaptive = ReadAdaptive(AdaptiveMarketEvaluator.Evaluate(series, assessedAt)),
                    candles = series
                        .Candles.TakeLast(21)
                        .Select(candle =>
                            new decimal[]
                            {
                                candle.Timestamp,
                                Normalize(candle.Open),
                                Normalize(candle.High),
                                Normalize(candle.Low),
                                Normalize(candle.Close),
                                Normalize(candle.Volume),
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
                evidenceAvailable = prioritySymbols.Contains(signal.Symbol)
                    && snapshot.Series.Any(series =>
                        series.Symbol == signal.Symbol
                        && CanVerify(series)
                        && series.Candles[^1].Timestamp == signal.CandleAt
                    ),
            }),
            rule = "Research alert only: closed 5-minute close outside previous 20-bar high/low, volume at least 1.5x previous 20-bar median, current spread at most 0.5%, fresh quote and consecutive verified candles. Thresholds are unvalidated heuristics; no backtest or net-profit proof. Never an order or approval.",
        };
    }

    private static Dictionary<string, object?> ReadAdaptive(AdaptiveMarketAssessment assessment)
    {
        var evidence = new Dictionary<string, object?>
        {
            ["available"] = assessment.Available,
            ["ruleVersion"] = assessment.RuleVersion,
            ["regime"] = assessment.Regime.ToString().ToLowerInvariant(),
            ["volatility"] = assessment.Volatility.ToString().ToLowerInvariant(),
            ["windowBars"] = assessment.UsedBars,
        };
        if (!assessment.Available)
        {
            evidence["reason"] = assessment.UnavailableReason;
            return evidence;
        }

        evidence["windowFrom"] = assessment.WindowFrom;
        evidence["candleAt"] = assessment.CandleAt;
        evidence["ema20"] = assessment.Ema20;
        evidence["ema50"] = assessment.Ema50;
        evidence["atr14"] = assessment.Atr14;
        evidence["atrPercent"] = RoundScreening(assessment.AtrPercent, 4);
        evidence["relativeAtr"] = RoundScreening(assessment.RelativeAtr, 4);
        evidence["trendGapAtr"] = RoundScreening(assessment.TrendGapAtr, 4);
        evidence["ema20SlopeAtr"] = RoundScreening(assessment.Ema20SlopeAtr, 4);
        return evidence;
    }

    private static decimal? RoundScreening(decimal? value, int places) =>
        value is decimal number ? Normalize(decimal.Round(number, places)) : null;

    private static decimal Normalize(decimal value) =>
        decimal.Parse(
            value.ToString("G29", CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture
        );

    private static bool CanVerify(IntradaySeries series)
    {
        var candles = series.Candles.TakeLast(21).ToArray();
        return series.Error is null
            && candles.Length == 21
            && candles
                .Skip(1)
                .Select((candle, index) => candle.Timestamp - candles[index].Timestamp)
                .All(interval => interval == 300);
    }

    private static string[] PrioritizeSeries(IReadOnlyList<MarketScanEntry> candidates, IntradaySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        var selected = snapshot
            .Signals.Select(signal => signal.Symbol)
            .Where(symbol => candidates.Any(entry => entry.Symbol == symbol))
            .Distinct()
            .ToList();
        var supplied = candidates
            .Where(entry =>
                snapshot.Series.Any(series =>
                    series.Symbol == entry.Symbol && series.Error is null && series.Candles.Count > 0
                )
            )
            .ToArray();
        if (supplied.Any(entry => entry.Symbol == "BTC_THB") && !selected.Contains("BTC_THB"))
        {
            selected.Add("BTC_THB");
        }

        var liquid = supplied
            .OrderBy(entry => entry.Range24h is <= 0.5m ? 1 : 0)
            .ThenByDescending(entry => entry.Volume24h)
            .ThenBy(entry => entry.Symbol)
            .ToArray();
        var movers = supplied
            .OrderByDescending(entry => Math.Abs(entry.Change24h ?? 0m) / (1m + (entry.SpreadPercent ?? 0m)))
            .ThenBy(entry => entry.Symbol)
            .ToArray();
        for (var index = 0; index < supplied.Length; index++)
        {
            foreach (var symbol in new[] { movers[index].Symbol, liquid[index].Symbol })
            {
                if (!selected.Contains(symbol))
                {
                    selected.Add(symbol);
                }
            }
        }

        return selected.Concat(candidates.Select(entry => entry.Symbol)).Distinct().ToArray();
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
                candleColumns = BitkubResearchMarketReader.candleColumns,
                numericEncoding = "Lossless decimal values; insignificant trailing zeros are omitted.",
                candles = retainedCandles.Select(candle =>
                    new decimal[]
                    {
                        candle.Timestamp,
                        Normalize(candle.Open),
                        Normalize(candle.High),
                        Normalize(candle.Low),
                        Normalize(candle.Close),
                        Normalize(candle.Volume),
                    }
                ),
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
