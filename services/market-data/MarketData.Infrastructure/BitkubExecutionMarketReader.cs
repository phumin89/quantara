using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class BitkubExecutionMarketReader : IExecutionMarketReader
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] errorFields = ["error", "code"];

    private const string historyUnavailable = "Fewer than 51 consecutive recent completed candles could be verified.";

    private readonly HttpClient client;

    private readonly IIntradayMarketReader? intraday;

    private readonly TimeProvider timeProvider;

    private readonly object universeLock = new();

    private (DateTimeOffset ExpiresAt, string Payload)? universeCache;

    private readonly ConcurrentDictionary<
        string,
        (long Slot, int Interval, IReadOnlyList<MarketCandle> Candles)
    > history = new(StringComparer.Ordinal);

    public BitkubExecutionMarketReader(
        HttpClient client,
        IIntradayMarketReader? intraday = null,
        TimeProvider? timeProvider = null
    )
    {
        this.client = client;
        this.intraday = intraday;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> Read(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool includeHistory = true,
        bool adaptiveTimeframes = false
    )
    {
        ValidateSymbols(symbols);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        JsonElement metadata;
        try
        {
            metadata = await this.Get("/api/v3/market/symbols", budget.Token);
            EnsureSuccess(metadata);
            if (metadata.GetProperty("result").ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Public market metadata is invalid.");
            }
        }
        catch (Exception exception) when (IsEvidenceFailure(exception, cancellationToken))
        {
            return this.Serialize(
                [],
                symbols.Select(symbol => (symbol, "Public market metadata could not be verified."))
            );
        }

        var snapshot = this.intraday?.Read();
        using var parallelism = new SemaphoreSlim(4);
        var results = await Task.WhenAll(
            symbols.Select(async symbol =>
            {
                try
                {
                    await parallelism.WaitAsync(budget.Token);
                    try
                    {
                        var market = await this.ReadMarket(
                            symbol,
                            metadata,
                            snapshot,
                            includeHistory,
                            adaptiveTimeframes,
                            budget.Token
                        );
                        return (market.Market, market.Evidence, Symbol: symbol, market.Error);
                    }
                    finally
                    {
                        parallelism.Release();
                    }
                }
                catch (Exception exception) when (IsEvidenceFailure(exception, cancellationToken))
                {
                    var error =
                        exception is OperationCanceledException ? "Public market evidence timed out."
                        : exception is InvalidDataException && exception.Message == historyUnavailable
                            ? historyUnavailable
                        : "Public market rules, order book or completed candles could not be verified.";
                    return (
                        Market: (object?)null,
                        Evidence: (IReadOnlyList<ExecutionHistoryEvidence>)[],
                        Symbol: symbol,
                        Error: (string?)error
                    );
                }
            })
        );
        return this.Serialize(
            results.Where(result => result.Market is not null).Select(result => result.Market),
            results.Where(result => result.Error is not null).Select(result => (result.Symbol, result.Error ?? "")),
            adaptiveTimeframes && includeHistory
                ? results.SelectMany(result => result.Evidence).Select(item => (object?)item)
                : null
        );
    }

    public async Task<string> ReadUniverse(CancellationToken cancellationToken)
    {
        lock (this.universeLock)
        {
            if (this.universeCache is { } cached && cached.ExpiresAt > this.timeProvider.GetUtcNow())
            {
                return cached.Payload;
            }
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var symbols = await this.Get("/api/v3/market/symbols", budget.Token);
            var ticker = await this.Get("/api/v3/market/ticker", budget.Token);
            EnsureUniverseMetadata(symbols);
            if (ticker.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Public ticker response is invalid.");
            }

            var scan = MarketUniverseScanner.Scan(symbols, ticker);
            var eligible = scan
                .Entries.Where(entry => entry.Usable && IsTradableRule(entry.SourceRule))
                .OrderByDescending(entry => entry.Volume24h)
                .ThenBy(entry => entry.Symbol, StringComparer.Ordinal)
                .ToArray();
            var observedAt = this.timeProvider.GetUtcNow();
            var payload = JsonSerializer.Serialize(
                new
                {
                    observedAt,
                    symbols = eligible.Select(entry => entry.Symbol[..^4]).ToArray(),
                    totalMarkets = scan.UniverseCount,
                    excludedMarkets = Math.Max(0, scan.UniverseCount - eligible.Length),
                    error = (string?)null,
                },
                jsonOptions
            );
            if (Encoding.UTF8.GetByteCount(payload) > 1_048_576)
            {
                throw new InvalidDataException("Public market universe exceeds its size limit.");
            }

            lock (this.universeLock)
            {
                this.universeCache = (observedAt.AddSeconds(60), payload);
            }

            return payload;
        }
        catch (Exception exception) when (IsEvidenceFailure(exception, cancellationToken))
        {
            return JsonSerializer.Serialize(
                new
                {
                    observedAt = this.timeProvider.GetUtcNow(),
                    symbols = Array.Empty<string>(),
                    totalMarkets = 0,
                    excludedMarkets = 0,
                    error = exception is OperationCanceledException
                        ? "Public market universe timed out."
                        : "Public market universe could not be verified.",
                },
                jsonOptions
            );
        }
    }

    public async Task<string> ReadHistorical(
        string symbol,
        int timeframeSeconds,
        long from,
        long until,
        CancellationToken cancellationToken
    )
    {
        if (symbol.Length is < 1 or > 30 || symbol.Any(c => c is not (>= 'A' and <= 'Z' or >= '0' and <= '9')))
        {
            throw new ArgumentException("Invalid symbol.", nameof(symbol));
        }

        if (timeframeSeconds is not (300 or 900 or 3600))
        {
            throw new ArgumentException("Timeframe must be 300, 900 or 3600 seconds.", nameof(timeframeSeconds));
        }

        if (
            from < 0
            || until <= from
            || until - from > 31L * 86400
            || (until - from) / timeframeSeconds > 999
            || from % timeframeSeconds != 0
            || until % timeframeSeconds != 0
            || until > this.timeProvider.GetUtcNow().ToUnixTimeSeconds() / timeframeSeconds * timeframeSeconds
        )
        {
            throw new ArgumentException("History window must be aligned, positive, and no longer than 31 days.");
        }

        var raw = await this.Get(
            $"/tradingview/history?symbol={symbol}_THB&resolution={timeframeSeconds / 60}&from={from}&to={until}",
            cancellationToken
        );
        var missing = raw.TryGetProperty("s", out var historyStatus) && historyStatus.GetString() == "no_data";
        var candles = missing ? [] : CandleParser.Parse(raw, from, until, timeframeSeconds, 1000);
        if (!missing && candles.Any(candle => candle.Timestamp % timeframeSeconds != 0))
        {
            throw new InvalidDataException(
                "Historical candle timestamp is not aligned to the native timeframe boundary."
            );
        }

        return JsonSerializer.Serialize(
            new
            {
                symbol,
                timeframeSeconds,
                source = "bitkub-tradingview",
                from,
                to = until,
                observedAt = this.timeProvider.GetUtcNow(),
                candles,
                error = missing ? "Historical candles are unavailable; gap cause unknown." : (string?)null,
            },
            jsonOptions
        );
    }

    private static bool IsTradableRule(JsonElement rule)
    {
        return rule.TryGetProperty("freeze_buy", out var buy)
            && rule.TryGetProperty("freeze_sell", out var sell)
            && bool.TryParse(buy.ToString(), out var frozenBuy)
            && bool.TryParse(sell.ToString(), out var frozenSell)
            && !frozenBuy
            && !frozenSell;
    }

    private static void EnsureUniverseMetadata(JsonElement root)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || errorFields.Any(field =>
                root.TryGetProperty(field, out var value)
                && (!decimal.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var code) || code != 0)
            )
        )
        {
            throw new InvalidDataException("Public market metadata is invalid.");
        }
    }

    private static void ValidateSymbols(IReadOnlyList<string> symbols)
    {
        if (
            symbols.Count is < 1 or > 16
            || symbols.Any(symbol =>
                symbol.Length is < 1 or > 30
                || symbol.Any(character => !char.IsAsciiDigit(character) && character is not (>= 'A' and <= 'Z'))
            )
            || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Count
        )
        {
            throw new ArgumentException("Provide one to sixteen unique uppercase ASCII base symbols.", nameof(symbols));
        }
    }

    private static bool IsEvidenceFailure(Exception exception, CancellationToken requestToken) =>
        exception
            is HttpRequestException
                or InvalidDataException
                or JsonException
                or InvalidOperationException
                or KeyNotFoundException
                or FormatException
                or OverflowException
        || exception is OperationCanceledException && !requestToken.IsCancellationRequested;

    private static void EnsureSuccess(JsonElement root)
    {
        if (
            root.ValueKind != JsonValueKind.Object
            || BitkubExecutionMarketReader.errorFields.Any(field =>
                root.TryGetProperty(field, out var value)
                && (!decimal.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var code) || code != 0)
            )
        )
        {
            throw new InvalidDataException("Public market response is invalid.");
        }
    }

    private static decimal Number(JsonElement value) =>
        decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new InvalidDataException("Public market number is invalid.");

    private static int Scale(JsonElement rule, string field)
    {
        var value = Number(rule.GetProperty(field));
        return value is >= 0 and <= 28 && value == decimal.Truncate(value)
            ? (int)value
            : throw new InvalidDataException("Public market precision is invalid.");
    }

    private static bool Frozen(JsonElement rule, string field) =>
        bool.TryParse(rule.GetProperty(field).ToString(), out var frozen)
            ? frozen
            : throw new InvalidDataException("Public market availability is invalid.");

    private static (decimal Price, decimal Quantity) Top(JsonElement book, string side)
    {
        var levels = book.GetProperty(side);
        if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() is < 1 or > 5)
        {
            throw new InvalidDataException("Public order book is empty or invalid.");
        }

        var top = levels[0];
        if (top.ValueKind != JsonValueKind.Array || top.GetArrayLength() != 2)
        {
            throw new InvalidDataException("Public order book level is invalid.");
        }

        var price = Number(top[0]);
        var quantity = Number(top[1]);
        return price > 0 && quantity > 0
            ? (price, quantity)
            : throw new InvalidDataException("Public order book has no usable top level.");
    }

    private async Task<(object? Market, IReadOnlyList<ExecutionHistoryEvidence> Evidence, string? Error)> ReadMarket(
        string symbol,
        JsonElement metadata,
        IntradaySnapshot? snapshot,
        bool includeHistory,
        bool adaptiveTimeframes,
        CancellationToken cancellationToken
    )
    {
        var rules = metadata
            .GetProperty("result")
            .EnumerateArray()
            .Where(rule =>
                rule.ValueKind == JsonValueKind.Object
                && rule.TryGetProperty("base_asset", out var baseAsset)
                && baseAsset.ToString() == symbol
                && rule.TryGetProperty("quote_asset", out var quoteAsset)
                && quoteAsset.ToString() == "THB"
            )
            .ToArray();
        if (
            rules.Length != 1
            || rules[0].GetProperty("status").GetString() != "active"
            || rules[0].GetProperty("market_segment").GetString() != "SPOT"
            || rules[0].GetProperty("source").GetString() != "exchange"
        )
        {
            throw new InvalidDataException("Active public THB spot market could not be verified.");
        }

        var rule = rules[0];
        var minimumNotional = Number(rule.GetProperty("min_quote_size"));
        var priceStep = Number(rule.GetProperty("price_step"));
        var priceScale = Scale(rule, "price_scale");
        var baseScale = Scale(rule, "base_asset_scale");
        var quoteScale = Scale(rule, "quote_asset_scale");
        var freezeBuy = Frozen(rule, "freeze_buy");
        var freezeSell = Frozen(rule, "freeze_sell");
        if (minimumNotional <= 0 || priceStep <= 0 || decimal.Round(priceStep, priceScale) != priceStep)
        {
            throw new InvalidDataException("Public market rules are invalid.");
        }

        var depth = await this.Get($"/api/v3/market/depth?sym={symbol}_THB&lmt=5", cancellationToken);
        var observedAt = this.timeProvider.GetUtcNow();
        EnsureSuccess(depth);
        var book = depth.TryGetProperty("result", out var nested) ? nested : depth;
        var bid = Top(book, "bids");
        var ask = Top(book, "asks");
        if (ask.Price < bid.Price)
        {
            throw new InvalidDataException("Public order book is crossed.");
        }

        var historyResult = includeHistory
            ? await this.ReadBars(symbol, snapshot, cancellationToken, adaptiveTimeframes)
            : new MarketHistory([], 300);
        var bars = historyResult.Candles;
        var timeframe = historyResult.TimeframeSeconds;
        if (adaptiveTimeframes && includeHistory && bars.Count < 51)
        {
            return (null, historyResult.Evidence ?? [], historyUnavailable);
        }

        var market = new Dictionary<string, object?>
        {
            ["symbol"] = symbol,
            ["observedAt"] = observedAt,
            ["bid"] = bid.Price,
            ["ask"] = ask.Price,
            ["bidQuantity"] = bid.Quantity,
            ["askQuantity"] = ask.Quantity,
            ["bars"] = bars,
            ["minimumNotional"] = minimumNotional,
            ["priceStep"] = priceStep,
            ["priceScale"] = priceScale,
            ["baseScale"] = baseScale,
            ["quoteScale"] = quoteScale,
            ["enabled"] = !freezeBuy && !freezeSell,
        };
        if (!includeHistory)
        {
            market["sellEnabled"] = !freezeSell;
        }

        if (adaptiveTimeframes && includeHistory)
        {
            market["timeframeSeconds"] = timeframe;
        }

        return (market, historyResult.Evidence ?? [], null);
    }

    private async Task<MarketHistory> ReadBars(
        string symbol,
        IntradaySnapshot? snapshot,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    )
    {
        var now = this.timeProvider.GetUtcNow();
        var to = now.ToUnixTimeSeconds() / 300 * 300;
        var from = to - 120 * 300;
        if (adaptiveTimeframes)
        {
            var evidence = new List<ExecutionHistoryEvidence>();
            foreach (var interval in new[] { 300, 900, 3600 })
            {
                var alignedTo = now.ToUnixTimeSeconds() / interval * interval;
                var fromAdaptive = alignedTo - 120L * interval;
                try
                {
                    var rows = await this.ReadBarsAt(symbol, fromAdaptive, alignedTo, interval, cancellationToken);
                    var selected = CompletedWindow(rows, fromAdaptive, alignedTo, interval);
                    var missing = 120 - rows.Select(bar => bar.Timestamp).Distinct().Count();
                    evidence.Add(
                        new(
                            symbol,
                            this.timeProvider.GetUtcNow(),
                            "bitkub-tradingview",
                            interval,
                            rows.Count,
                            selected.Length,
                            missing,
                            selected.Length >= 51 ? "ready" : "unavailable",
                            missing == 0 ? "none" : "unknown",
                            selected.Length >= 51 ? null : historyUnavailable,
                            rows.Count(bar => bar.Volume == 0)
                        )
                    );
                    if (selected.Length >= 51)
                    {
                        return new MarketHistory(selected, interval, evidence);
                    }
                }
                catch (Exception error) when (IsEvidenceFailure(error, cancellationToken))
                {
                    evidence.Add(
                        new(
                            symbol,
                            this.timeProvider.GetUtcNow(),
                            "bitkub-tradingview",
                            interval,
                            0,
                            0,
                            120,
                            "unavailable",
                            error is HttpRequestException or OperationCanceledException
                                ? "source-unavailable"
                                : "unknown",
                            "Native completed history could not be verified."
                        )
                    );
                }
            }

            return new MarketHistory([], 300, evidence);
        }

        var retained = snapshot?.Series.SingleOrDefault(series => series.Symbol == symbol + "_THB");
        if (
            snapshot is { Available: true, Error: null }
            && retained is not null
            && retained.Error is null
            && retained.FetchedAt <= now
            && now - retained.FetchedAt <= TimeSpan.FromMinutes(5)
        )
        {
            var completed = CompletedWindow(retained.Candles, from, to);
            if (completed.Length >= 51)
            {
                return new MarketHistory(completed, 300);
            }
        }

        if (this.history.TryGetValue(symbol, out var cached) && cached.Slot == to && cached.Interval == 300)
        {
            return new MarketHistory(cached.Candles, 300);
        }

        var raw = await this.Get(
            $"/tradingview/history?symbol={symbol}_THB&resolution=5&from={from}&to={to}",
            cancellationToken
        );
        if (
            raw.TryGetProperty("s", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString() == "no_data"
        )
        {
            throw new InvalidDataException(historyUnavailable);
        }

        var candles = CompletedWindow(CandleParser.Parse(raw, from, to, 300, 121), from, to);
        if (candles.Length < 51)
        {
            throw new InvalidDataException(historyUnavailable);
        }

        if (this.history.Count >= 256)
        {
            this.history.Clear();
        }

        var verified = Array.AsReadOnly(candles);
        this.history[symbol] = (to, 300, verified);
        return new MarketHistory(verified, 300);
    }

    private async Task<IReadOnlyList<MarketCandle>> ReadBarsAt(
        string symbol,
        long from,
        long to,
        int interval,
        CancellationToken cancellationToken
    )
    {
        var key = $"{symbol}:{interval}:{to}";
        if (this.history.TryGetValue(key, out var cached) && cached.Slot == to && cached.Interval == interval)
        {
            return cached.Candles;
        }

        var raw = await this.Get(
            $"/tradingview/history?symbol={symbol}_THB&resolution={interval / 60}&from={from}&to={to}",
            cancellationToken
        );
        if (raw.TryGetProperty("s", out var status) && status.GetString() == "no_data")
        {
            throw new InvalidDataException("no_data");
        }

        var candles = CandleParser.Parse(raw, from, to, interval, 121).TakeLast(120).ToArray();
        if (CompletedWindow(candles, from, to, interval).Length < 51)
        {
            return candles;
        }

        if (this.history.Count >= 256)
        {
            this.history.Clear();
        }

        this.history[key] = (to, interval, Array.AsReadOnly(candles));
        return candles;
    }

    private static MarketCandle[] CompletedWindow(
        IReadOnlyList<MarketCandle> source,
        long from,
        long to,
        long interval = 300
    )
    {
        var completed = source
            .Where(candle => candle.Timestamp >= from && candle.Timestamp < to)
            .TakeLast(120)
            .ToArray();
        if (completed.Length == 0 || completed[^1].Timestamp != to - interval)
        {
            return [];
        }

        var start = completed.Length - 1;
        while (start > 0 && completed[start].Timestamp - completed[start - 1].Timestamp == interval)
        {
            start--;
        }

        return completed[start..];
    }

    private async Task<JsonElement> Get(string path, CancellationToken cancellationToken)
    {
        using var response = await this.client.GetAsync(
            path,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1_048_576)
        {
            throw new InvalidDataException("Public market response exceeds its size limit.");
        }

        await response.Content.LoadIntoBufferAsync(1_048_576, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    private string Serialize(
        IEnumerable<object?> markets,
        IEnumerable<(string Symbol, string Error)> errors,
        IEnumerable<object?>? evidence = null
    )
    {
        var payload = new Dictionary<string, object?>
        {
            ["observedAt"] = this.timeProvider.GetUtcNow(),
            ["markets"] = markets,
            ["errors"] = errors.Select(error => new { symbol = error.Symbol, error = error.Error }),
        };
        if (evidence is not null)
        {
            payload["evidence"] = evidence;
        }

        var result = JsonSerializer.Serialize(payload, BitkubExecutionMarketReader.jsonOptions);
        return Encoding.UTF8.GetByteCount(result) <= 1_048_576
            ? result
            : throw new InvalidDataException("Public market evidence exceeds its size limit.");
    }
}
