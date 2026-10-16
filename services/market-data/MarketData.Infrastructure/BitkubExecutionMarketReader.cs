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

    private readonly HttpClient client;

    private readonly IIntradayMarketReader? intraday;

    private readonly TimeProvider timeProvider;

    private readonly object universeLock = new();

    private (DateTimeOffset ExpiresAt, string Payload)? universeCache;

    private readonly ConcurrentDictionary<string, (long Slot, IReadOnlyList<MarketCandle> Candles)> history = new(
        StringComparer.Ordinal
    );

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
        bool includeHistory = true
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
                        var market = await this.ReadMarket(symbol, metadata, snapshot, includeHistory, budget.Token);
                        return (Market: market, Symbol: symbol, Error: (string?)null);
                    }
                    finally
                    {
                        parallelism.Release();
                    }
                }
                catch (Exception exception) when (IsEvidenceFailure(exception, cancellationToken))
                {
                    var error =
                        exception is OperationCanceledException
                            ? "Public market evidence timed out."
                            : "Public market rules, order book or completed candles could not be verified.";
                    return (Market: (object?)null, Symbol: symbol, Error: error);
                }
            })
        );
        return this.Serialize(
            results.Where(result => result.Market is not null).Select(result => result.Market),
            results.Where(result => result.Error is not null).Select(result => (result.Symbol, result.Error ?? ""))
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

    private async Task<object?> ReadMarket(
        string symbol,
        JsonElement metadata,
        IntradaySnapshot? snapshot,
        bool includeHistory,
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

        var bars = includeHistory ? await this.ReadBars(symbol, snapshot, cancellationToken) : [];
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

        return market;
    }

    private async Task<IReadOnlyList<MarketCandle>> ReadBars(
        string symbol,
        IntradaySnapshot? snapshot,
        CancellationToken cancellationToken
    )
    {
        var now = this.timeProvider.GetUtcNow();
        var to = now.ToUnixTimeSeconds() / 300 * 300;
        var from = to - 120 * 300;
        var retained = snapshot?.Series.SingleOrDefault(series => series.Symbol == symbol + "_THB");
        if (
            snapshot is { Available: true, Error: null }
            && retained is not null
            && retained.Error is null
            && retained.FetchedAt <= now
            && now - retained.FetchedAt <= TimeSpan.FromMinutes(5)
        )
        {
            var completed = retained
                .Candles.Where(candle => candle.Timestamp >= from && candle.Timestamp < to)
                .TakeLast(120)
                .ToArray();
            if (completed.Length >= 51 && completed[^1].Timestamp == to - 300)
            {
                return completed;
            }
        }

        if (this.history.TryGetValue(symbol, out var cached) && cached.Slot == to)
        {
            return cached.Candles;
        }

        var raw = await this.Get(
            $"/tradingview/history?symbol={symbol}_THB&resolution=5&from={from}&to={to}",
            cancellationToken
        );
        var candles = CandleParser.Parse(raw, from, to, 300, 121).TakeLast(120).ToArray();
        if (candles.Length < 51 || candles[^1].Timestamp != to - 300)
        {
            throw new InvalidDataException("Sufficient recent completed candles could not be verified.");
        }

        if (this.history.Count >= 256)
        {
            this.history.Clear();
        }

        var verified = Array.AsReadOnly(candles);
        this.history[symbol] = (to, verified);
        return verified;
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

    private string Serialize(IEnumerable<object?> markets, IEnumerable<(string Symbol, string Error)> errors)
    {
        var result = JsonSerializer.Serialize(
            new
            {
                observedAt = this.timeProvider.GetUtcNow(),
                markets,
                errors = errors.Select(error => new { symbol = error.Symbol, error = error.Error }),
            },
            BitkubExecutionMarketReader.jsonOptions
        );
        return Encoding.UTF8.GetByteCount(result) <= 1_048_576
            ? result
            : throw new InvalidDataException("Public market evidence exceeds its size limit.");
    }
}
