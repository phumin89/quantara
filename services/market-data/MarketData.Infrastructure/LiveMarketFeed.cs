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

    private readonly MarketEventBus? eventStream;

    private readonly object sync = new();

    private readonly HashSet<string> tickerConnections = [];

    private readonly Dictionary<string, (string Json, DateTimeOffset ObservedAt)> bookEvents = [];

    private readonly Dictionary<string, PublicEventBook> executableBooks = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> ruleSignatures = new(StringComparer.Ordinal);

    private JsonElement? metadata;

    private JsonElement? tickers;

    private DateTimeOffset metadataAt;

    private IReadOnlyList<MarketWatchSymbol> watch = [];

    private IReadOnlyList<LiveMarketQuote> quotes = [];

    private IntradaySnapshot snapshot;

    public LiveMarketFeed(
        HttpClient client,
        IIntradayCandleStore store,
        TimeProvider timeProvider,
        MarketEventBus? eventStream = null
    )
    {
        this.client = client;
        this.store = store;
        this.timeProvider = timeProvider;
        this.eventStream = eventStream;
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
                this.SetSeries(symbol, retained, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                this.SetSeries(symbol, [], "Intraday candle data is unavailable.");
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
        var sessions =
            new Dictionary<(string Symbol, int PairingId), (CancellationTokenSource Cancellation, Task Running)>();
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do
            {
                try
                {
                    await this.RefreshWatch(cancellationToken);
                    var desired = this.watch.ToArray();
                    await LiveMarketFeed.ReconcileSessions(
                        desired,
                        sessions,
                        (symbol, token) =>
                            Task.WhenAll(this.ReceiveTicker(symbol, token), this.ReceiveBook(symbol, token)),
                        cancellationToken
                    );
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    this.SetConnected(false, "Live market watch refresh is unavailable.");
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                await session.Cancellation.CancelAsync();
            }

            try
            {
                await Task.WhenAll(sessions.Values.Select(session => session.Running));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            finally
            {
                foreach (var session in sessions.Values)
                {
                    session.Cancellation.Dispose();
                }
            }
        }
    }

    private static async Task ReconcileSessions(
        IReadOnlyList<MarketWatchSymbol> desired,
        Dictionary<(string Symbol, int PairingId), (CancellationTokenSource Cancellation, Task Running)> sessions,
        Func<MarketWatchSymbol, CancellationToken, Task> start,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var removed in sessions
                .Keys.Where(symbol =>
                    !desired.Any(item => item.Symbol == symbol.Symbol && item.PairingId == symbol.PairingId)
                )
                .ToArray()
        )
        {
            var previous = sessions[removed];
            await previous.Cancellation.CancelAsync();
            try
            {
                await previous.Running;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            previous.Cancellation.Dispose();
            sessions.Remove(removed);
        }

        foreach (var symbol in desired.Where(symbol => !sessions.ContainsKey((symbol.Symbol, symbol.PairingId))))
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sessions[(symbol.Symbol, symbol.PairingId)] = (linked, start(symbol, linked.Token));
        }
    }

    private async Task Receive(ClientWebSocket socket, MarketWatchSymbol source, CancellationToken cancellationToken)
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

            this.ParseMessage(source, Encoding.UTF8.GetString(message.ToArray()));
        }
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
                this.SetTickerConnection(symbol, true);
                delay = TimeSpan.FromSeconds(1);
                await this.Receive(socket, symbol, cancellationToken);
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
                this.SetTickerConnection(symbol, false);
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
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
                // A malformed or closed book must not retain prices across reconnects.
            }
            finally
            {
                this.SetDepth(symbol, null);
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
                this.SetDepth(symbol, json);
            }
        }
    }

    private async Task RefreshWatch(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        if (this.metadata is null || this.tickers is null || now - this.metadataAt >= TimeSpan.FromMinutes(1))
        {
            this.metadata = await this.Get("/api/v3/market/symbols", cancellationToken);
            this.tickers = await this.Get("/api/v3/market/ticker", cancellationToken);
            this.metadataAt = this.timeProvider.GetUtcNow();
        }

        var symbols = this.metadata.Value;
        var ticker = this.tickers.Value;
        var required = this.eventStream?.RequiredSymbols ?? [];
        var selected = MarketWatchSelection.Select(symbols, ticker, required, now);
        var rules = symbols
            .GetProperty("result")
            .EnumerateArray()
            .Where(MarketUniverseScanner.IsEligibleRule)
            .DistinctBy(rule => rule.GetRawText())
            .ToArray();
        foreach (var missing in required.Where(symbol => !selected.Any(item => item.Symbol == symbol + "_THB")))
        {
            this.eventStream?.Publish(MarketEventKind.FeedUnavailable, missing, now);
        }

        lock (this.sync)
        {
            // Retire ticker state while the previous watch still authorizes its cleanup.
            foreach (
                var retired in this.watch.Where(current =>
                    !selected.Any(item => item.Symbol == current.Symbol && item.PairingId == current.PairingId)
                )
            )
            {
                this.SetTickerConnection(retired, false);
            }

            this.watch = selected;
            foreach (var symbol in selected)
            {
                var rule = rules.Single(rule =>
                    string.Equals(
                        MarketUniverseScanner.Text(rule, "base_asset"),
                        symbol.Symbol.Split('_')[0],
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                var signature = rule.GetRawText();
                if (!this.ruleSignatures.TryGetValue(symbol.Symbol, out var previous) || signature != previous)
                {
                    this.executableBooks[symbol.Symbol] = new PublicEventBook(
                        symbol.Symbol.Split('_')[0],
                        symbol.PairingId,
                        rule
                    );
                    this.ruleSignatures[symbol.Symbol] = signature;
                    if (previous is not null)
                    {
                        this.SetDepth(symbol, null);
                    }
                }
            }

            var active = selected.Select(symbol => symbol.Symbol).ToHashSet(StringComparer.Ordinal);
            this.quotes = this.quotes.Where(quote => active.Contains(quote.Symbol)).ToArray();
            foreach (var removed in this.executableBooks.Keys.Where(symbol => !active.Contains(symbol)).ToArray())
            {
                this.executableBooks.Remove(removed);
                this.ruleSignatures.Remove(removed);
                this.bookEvents.Remove(removed);
            }

            this.snapshot = this.snapshot with
            {
                Series = this.snapshot.Series.Where(series => active.Contains(series.Symbol)).ToArray(),
                Signals = this.snapshot.Signals.Where(signal => active.Contains(signal.Symbol)).ToArray(),
            };
        }

        if (this.eventStream is not null)
        {
            await this.eventStream.SetWatched(
                selected.Select(symbol => symbol.Symbol.Split('_')[0]).ToArray(),
                cancellationToken
            );
        }
    }

    private void ParseMessage(MarketWatchSymbol source, string json)
    {
        var quote = PublicTickerParser.Parse(json, this.timeProvider.GetUtcNow());
        if (quote is null || quote.Symbol != source.Symbol)
        {
            return;
        }

        lock (this.sync)
        {
            if (!this.watch.Any(item => item.Symbol == source.Symbol && item.PairingId == source.PairingId))
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
            this.eventStream?.Publish(MarketEventKind.QuoteChanged, quote.Symbol.Split('_')[0], quote.ObservedAt);
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

    private void SetDepth(MarketWatchSymbol source, string? json)
    {
        lock (this.sync)
        {
            if (!this.watch.Any(item => item.Symbol == source.Symbol && item.PairingId == source.PairingId))
            {
                return;
            }

            var symbol = source.Symbol;
            var observedAt = this.timeProvider.GetUtcNow();
            if (json is null)
            {
                this.bookEvents.Remove(symbol);
                if (this.executableBooks.TryGetValue(symbol, out var disconnectedBook))
                {
                    disconnectedBook.Reset();
                }
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
            var book =
                json is not null && this.executableBooks.TryGetValue(symbol, out var parser)
                    ? parser.Apply(json, observedAt)
                    : null;

            this.eventStream?.Publish(
                json is null ? MarketEventKind.FeedUnavailable : MarketEventKind.BookChanged,
                symbol.Split('_')[0],
                observedAt,
                book
            );
        }
    }

    private void SetTickerConnection(MarketWatchSymbol source, bool connected)
    {
        lock (this.sync)
        {
            if (!this.watch.Any(item => item.Symbol == source.Symbol && item.PairingId == source.PairingId))
            {
                return;
            }

            var symbol = source.Symbol;
            if (connected)
            {
                this.tickerConnections.Add(symbol);
            }
            else
            {
                this.tickerConnections.Remove(symbol);
                this.quotes = this.quotes.Where(quote => quote.Symbol != symbol).ToArray();
                this.eventStream?.Publish(
                    MarketEventKind.FeedUnavailable,
                    symbol.Split('_')[0],
                    this.timeProvider.GetUtcNow()
                );
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

    private void SetSeries(MarketWatchSymbol source, IReadOnlyList<MarketCandle> candles, string? error)
    {
        lock (this.sync)
        {
            if (!this.watch.Any(item => item.Symbol == source.Symbol && item.PairingId == source.PairingId))
            {
                return;
            }

            var symbol = source.Symbol;
            var series = this
                .snapshot.Series.Where(x => x.Symbol != symbol)
                .Append(new IntradaySeries(symbol, candles, this.timeProvider.GetUtcNow(), error))
                .ToArray();
            this.snapshot = this.snapshot with { Series = series };
            if (candles.Count > 0)
            {
                this.eventStream?.Publish(
                    MarketEventKind.CandleClosed,
                    symbol.Split('_')[0],
                    this.timeProvider.GetUtcNow(),
                    candleAt: candles[^1].Timestamp
                );
            }
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
            if (!connected)
            {
                foreach (var symbol in this.watch)
                {
                    this.eventStream?.Publish(
                        MarketEventKind.FeedUnavailable,
                        symbol.Symbol.Split('_')[0],
                        this.timeProvider.GetUtcNow()
                    );
                }
            }
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
