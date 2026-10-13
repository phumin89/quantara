using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class LiveMarketFeed : BackgroundService, IIntradayMarketReader
{
    private readonly HttpClient client;

    private readonly IIntradayCandleStore store;

    private readonly TimeProvider timeProvider;

    private readonly object sync = new();

    private readonly HashSet<string> tickerConnections = [];

    private readonly Dictionary<string, (string Json, DateTimeOffset ObservedAt)> bookEvents = [];

    private IReadOnlyList<MarketWatchSymbol> watch = [];

    private IReadOnlyList<LiveMarketQuote> quotes = [];

    private IntradaySnapshot snapshot;

    public LiveMarketFeed(HttpClient client, IIntradayCandleStore store, TimeProvider timeProvider)
    {
        this.client = client;
        this.store = store;
        this.timeProvider = timeProvider;
        this.snapshot = new IntradaySnapshot(
            false,
            DateTimeOffset.MinValue,
            false,
            [],
            [],
            [],
            "Feed has not started."
        );
    }

    public IntradaySnapshot Read()
    {
        lock (this.sync)
        {
            var now = this.timeProvider.GetUtcNow();
            var freshSymbols = this
                .quotes.Where(quote =>
                    this.tickerConnections.Contains(quote.Symbol) && now - quote.ObservedAt <= TimeSpan.FromSeconds(60)
                )
                .Select(quote => quote.Symbol)
                .ToHashSet(StringComparer.Ordinal);
            var freshSeries = this
                .snapshot.Series.Where(series =>
                    freshSymbols.Contains(series.Symbol)
                    && series.Error is null
                    && series.Candles.Count > 0
                    && now - series.FetchedAt <= TimeSpan.FromMinutes(2)
                )
                .ToArray();
            var available = freshSeries.Length > 0;
            return this.snapshot with
            {
                Available = available,
                Quotes = this.quotes,
                Signals =
                    available && now - this.snapshot.CheckedAt <= TimeSpan.FromMinutes(2)
                        ? this
                            .snapshot.Signals.Where(signal =>
                                freshSeries.Any(series =>
                                    series.Symbol == signal.Symbol && series.Candles[^1].Timestamp == signal.CandleAt
                                )
                                && now - signal.DetectedAt <= TimeSpan.FromMinutes(2)
                            )
                            .ToArray()
                        : [],
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.store.Initialize(stoppingToken);
                await this.RefreshWatch(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                this.SetError("Candle storage is unavailable.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var socket = this.RunSocket(linked.Token);
        var candles = this.RunCandles(linked.Token);
        await Task.WhenAll(socket, candles);
    }

    private async Task RunCandles(CancellationToken cancellationToken)
    {
        await this.RefreshCandles(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await this.RefreshCandles(cancellationToken);
        }
    }

    private async Task RefreshCandles(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var to = now.ToUnixTimeSeconds();
        var from = to - 8 * 3600;
        foreach (var symbol in this.watch)
        {
            try
            {
                var root = await this.Get(
                    $"/tradingview/history?symbol={symbol.Symbol}&resolution=5&from={from}&to={to}",
                    cancellationToken
                );
                var candles = CandleParser.Parse(root, from, to, 300, 1000);
                await this.store.Save(symbol.Symbol, candles, cancellationToken);
                var retained = await this.store.Read(symbol.Symbol, cancellationToken);
                this.SetSeries(symbol.Symbol, retained, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                this.SetSeries(symbol.Symbol, [], "Intraday candle data is unavailable.");
            }
        }

        lock (this.sync)
        {
            var checkedAt = this.timeProvider.GetUtcNow();
            var signals = this
                .snapshot.Series.Select(series =>
                {
                    var quote = this.quotes.FirstOrDefault(item => item.Symbol == series.Symbol);
                    return quote is null || !this.tickerConnections.Contains(series.Symbol)
                        ? null
                        : IntradaySignalEvaluator.Evaluate(quote, series, checkedAt);
                })
                .OfType<MarketSignal>()
                .ToArray();
            this.snapshot = this.snapshot with
            {
                CheckedAt = checkedAt,
                Signals = signals,
                Error = this.snapshot.Series.Any(series => series.Error is null)
                    ? null
                    : "Intraday candle data is unavailable.",
            };
        }
    }

    private async Task RunSocket(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await this.RefreshWatch(cancellationToken);
                delay = TimeSpan.FromSeconds(1);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                session.CancelAfter(TimeSpan.FromHours(1));
                await Task.WhenAll(this.RunTickers(session.Token), this.RunBooks(session.Token));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                this.SetConnected(false, "Live market feed is unavailable.");
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task Receive(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > 65536)
                {
                    throw new InvalidDataException("Bitkub websocket message is too large.");
                }
            } while (!result.EndOfMessage);

            this.ParseMessage(Encoding.UTF8.GetString(message.ToArray()));
        }
    }

    private async Task RunTickers(CancellationToken cancellationToken)
    {
        await Task.WhenAll(this.watch.Select(symbol => this.ReceiveTicker(symbol, cancellationToken)));
    }

    private async Task ReceiveTicker(MarketWatchSymbol symbol, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(
                    new Uri(
                        $"wss://api.bitkub.com/websocket-api/market.ticker.thb_{symbol.Symbol.Split('_')[0].ToLowerInvariant()}"
                    ),
                    cancellationToken
                );
                this.SetTickerConnection(symbol.Symbol, true);
                delay = TimeSpan.FromSeconds(1);
                await this.Receive(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Other markets continue when one ticker disconnects.
            }
            finally
            {
                this.SetTickerConnection(symbol.Symbol, false);
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private async Task RunBooks(CancellationToken cancellationToken)
    {
        await Task.WhenAll(this.watch.Select(symbol => this.ReceiveBook(symbol, cancellationToken)));
    }

    private async Task ReceiveBook(MarketWatchSymbol symbol, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await this.ReadBook(symbol, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                this.SetDepth(symbol.Symbol, null);
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    private async Task ReadBook(MarketWatchSymbol symbol, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            new Uri($"wss://api.bitkub.com/websocket-api/orderbook/{symbol.PairingId}"),
            cancellationToken
        );
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > 12000)
                {
                    throw new InvalidDataException("Bitkub order book event is too large.");
                }
            } while (!result.EndOfMessage);

            var json = PublicBookParser.Parse(Encoding.UTF8.GetString(message.ToArray()), symbol.PairingId);
            if (json is not null)
            {
                this.SetDepth(symbol.Symbol, json);
            }
        }
    }

    private async Task RefreshWatch(CancellationToken cancellationToken)
    {
        var symbols = await this.Get("/api/v3/market/symbols", cancellationToken);
        var ticker = await this.Get("/api/v3/market/ticker", cancellationToken);
        var eligible = symbols
            .GetProperty("result")
            .EnumerateArray()
            .Where(x =>
                Text(x, "quote_asset") == "THB"
                && Text(x, "status") == "active"
                && Text(x, "market_segment") == "SPOT"
                && Text(x, "source") == "exchange"
            )
            .ToDictionary(x => Text(x, "base_asset").ToUpperInvariant(), x => (int)Number(x, "pairing_id"));
        var selected = ticker
            .EnumerateArray()
            .Where(x =>
                Text(x, "symbol").EndsWith("_THB", StringComparison.Ordinal)
                && eligible.ContainsKey(Text(x, "symbol").Split('_')[0].ToUpperInvariant())
            )
            .OrderByDescending(x => Number(x, "quote_volume"))
            .Take(5)
            .Select(x => new MarketWatchSymbol(
                Text(x, "symbol").ToUpperInvariant(),
                eligible[Text(x, "symbol").Split('_')[0].ToUpperInvariant()],
                Number(x, "quote_volume")
            ))
            .ToList();
        if (!selected.Any(x => x.Symbol == "BTC_THB") && eligible.TryGetValue("BTC", out var btcPair))
        {
            if (selected.Count == 5)
            {
                selected.RemoveAt(4);
            }

            selected.Add(new MarketWatchSymbol("BTC_THB", btcPair, 0m));
        }

        lock (this.sync)
        {
            this.watch = selected;
            var activeSymbols = selected.Select(symbol => symbol.Symbol).ToHashSet(StringComparer.Ordinal);
            this.quotes = this.quotes.Where(quote => activeSymbols.Contains(quote.Symbol)).ToArray();
            foreach (var symbol in this.bookEvents.Keys.Where(symbol => !activeSymbols.Contains(symbol)).ToArray())
            {
                this.bookEvents.Remove(symbol);
            }

            this.snapshot = this.snapshot with
            {
                Series = this.snapshot.Series.Where(series => activeSymbols.Contains(series.Symbol)).ToArray(),
                Signals = [],
            };
        }
    }

    private void ParseMessage(string json)
    {
        var quote = PublicTickerParser.Parse(json, this.timeProvider.GetUtcNow());
        if (quote is null)
        {
            return;
        }

        lock (this.sync)
        {
            if (!this.watch.Any(item => item.Symbol == quote.Symbol))
            {
                return;
            }

            var previous = this.quotes.FirstOrDefault(item => item.Symbol == quote.Symbol);
            var hasBook = this.bookEvents.TryGetValue(quote.Symbol, out var book);
            quote = quote with
            {
                BookEventJson = hasBook ? book.Json : previous?.BookEventJson,
                BookObservedAt = hasBook ? book.ObservedAt : previous?.BookObservedAt,
            };
            this.quotes = this.quotes.Where(item => item.Symbol != quote.Symbol).Append(quote).ToArray();
            this.snapshot = this.snapshot with { Quotes = this.quotes };
        }
    }

    private async Task<JsonElement> Get(string path, CancellationToken cancellationToken)
    {
        using var response = await this.client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken
        );
        return document.RootElement.Clone();
    }

    private void SetDepth(string symbol, string? json)
    {
        lock (this.sync)
        {
            var observedAt = this.timeProvider.GetUtcNow();
            if (json is null)
            {
                this.bookEvents.Remove(symbol);
            }
            else
            {
                this.bookEvents[symbol] = (json, observedAt);
            }

            this.quotes = this
                .quotes.Select(quote =>
                    quote.Symbol == symbol
                        ? quote with
                        {
                            BookEventJson = json,
                            BookObservedAt = json is null ? null : observedAt,
                        }
                        : quote
                )
                .ToArray();
            this.snapshot = this.snapshot with { Quotes = this.quotes };
        }
    }

    private void SetTickerConnection(string symbol, bool connected)
    {
        lock (this.sync)
        {
            if (connected)
            {
                this.tickerConnections.Add(symbol);
            }
            else
            {
                this.tickerConnections.Remove(symbol);
                this.quotes = this.quotes.Where(quote => quote.Symbol != symbol).ToArray();
            }

            this.snapshot = this.snapshot with
            {
                Connected = this.tickerConnections.Count > 0,
                Quotes = this.quotes,
                Signals = this
                    .snapshot.Signals.Where(signal => this.tickerConnections.Contains(signal.Symbol))
                    .ToArray(),
            };
        }
    }

    private void SetSeries(string symbol, IReadOnlyList<MarketCandle> candles, string? error)
    {
        lock (this.sync)
        {
            var series = this
                .snapshot.Series.Where(x => x.Symbol != symbol)
                .Append(new IntradaySeries(symbol, candles, this.timeProvider.GetUtcNow(), error))
                .ToArray();
            this.snapshot = this.snapshot with { Series = series };
        }
    }

    private void SetConnected(bool connected, string? error)
    {
        lock (this.sync)
        {
            this.snapshot = this.snapshot with
            {
                Connected = connected,
                Available = connected && this.snapshot.Series.Any(x => x.Error is null && x.Candles.Count > 0),
                CheckedAt = this.timeProvider.GetUtcNow(),
                Error = error,
                Signals = connected ? this.snapshot.Signals : [],
            };
        }
    }

    private void SetError(string error)
    {
        lock (this.sync)
        {
            this.snapshot = this.snapshot with
            {
                Available = false,
                Error = error,
                CheckedAt = this.timeProvider.GetUtcNow(),
                Signals = [],
            };
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static decimal Number(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number
            : decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number
            : throw new InvalidDataException($"Invalid {property}.");
    }
}
