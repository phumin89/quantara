using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingWorker(
    TradingEngine engine,
    TradingReportDelivery reports,
    ITradingEventSource source,
    TradingEventDispatcher events,
    IReadOnlyTradingStore store,
    ITradingMarketReader markets,
    AutonomousMarketMonitor monitor,
    TimeProvider time,
    ILogger<TradingWorker> logger
) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "TradingEventUnavailable"),
        "Trading event work unavailable; retained claims remain authoritative."
    );

    private readonly object connectionGate = new();

    private readonly object quoteGate = new();

    private readonly ConcurrentDictionary<string, DateTimeOffset> quoteReads = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Task> quoteTasks = new(StringComparer.Ordinal);

    private CancellationTokenSource? connection;

    private string[] owned = [];

    private string[] required = [];

    private Task reconciling = Task.CompletedTask;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(
            this.Listen(stoppingToken),
            this.Dispatch(stoppingToken),
            this.Maintain(stoppingToken),
            this.Deliver(stoppingToken)
        );

    private async Task Listen(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (this.connectionGate)
            {
                this.connection = linked;
            }

            try
            {
                await foreach (var item in source.Read(Volatile.Read(ref this.required), linked.Token))
                {
                    events.SetFeed(true, time.GetUtcNow(), item.WatchedSymbols);
                    if (item.Kind != TradingEventKind.Heartbeat)
                    {
                        var ownedSymbol = Volatile.Read(ref this.owned).Contains(item.Symbol, StringComparer.Ordinal);
                        if (
                            item.Kind == TradingEventKind.CandleClosed
                            || ownedSymbol
                                && (
                                    item.Book is not null || item.Dirty || item.Kind == TradingEventKind.FeedUnavailable
                                )
                        )
                        {
                            events.Enqueue(item, ownedSymbol);
                        }
                        else
                        {
                            events.Observe(item);
                        }

                        if (
                            ownedSymbol
                            && item.Book is null
                            && item.Kind
                                is TradingEventKind.QuoteChanged
                                    or TradingEventKind.BookChanged
                                    or TradingEventKind.FeedUnavailable
                        )
                        {
                            this.RefreshQuote(item.Symbol, linked.Token);
                        }
                    }
                    else
                    {
                        events.Observe(item);
                    }

                    failures = 0;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed(logger, null);
            }
            finally
            {
                lock (this.connectionGate)
                {
                    this.connection = null;
                }

                events.SetFeed(false, time.GetUtcNow(), events.Snapshot().WatchedSymbols);
                foreach (var symbol in Volatile.Read(ref this.owned))
                {
                    events.Enqueue(
                        new(TradingEventKind.FeedUnavailable, symbol, 0, time.GetUtcNow(), dirty: true),
                        true
                    );
                }
            }

            if (!linked.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(5, failures++)))),
                    cancellationToken
                );
            }
        }
    }

    private async Task Dispatch(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var item = await events.Dequeue(cancellationToken);
            if (item is null)
            {
                continue;
            }

            try
            {
                if (
                    item.Dirty
                    && (
                        events.LatestBook(item.Symbol) is not { } quote
                        || time.GetUtcNow() - quote.ObservedAt > TimeSpan.FromSeconds(30)
                    )
                )
                {
                    this.RefreshQuote(item.Symbol, cancellationToken);
                }

                await engine.Process(item, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed(logger, null);
            }
        }
    }

    private async Task Maintain(CancellationToken cancellationToken)
    {
        var tick = 0;
        var version = -1L;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                if (tick % 2 == 0)
                {
                    var state = await store.Read(cancellationToken);
                    var owned = state
                        .Live.Positions.Concat(state.Paper.Positions)
                        .Where(position => position.Quantity > 0)
                        .Select(position => position.Symbol)
                        .Concat(
                            state
                                .Live.Orders.Concat(state.Paper.Orders)
                                .Where(TradingLedgerAccounting.Active)
                                .Select(order => order.Symbol)
                        )
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var required = owned
                        .Concat(state.Rules.Adaptive is null ? state.Rules.Symbols : [])
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    Volatile.Write(ref this.owned, owned);
                    if (!required.SequenceEqual(Volatile.Read(ref this.required)))
                    {
                        Volatile.Write(ref this.required, required);
                        lock (this.connectionGate)
                        {
                            this.connection?.Cancel();
                        }
                    }
                }

                if (tick % 5 == 0)
                {
                    events.EnqueueMaintenance();
                }

                if (tick % 60 == 0)
                {
                    events.Enqueue(new(TradingEventKind.Research, "", 0, time.GetUtcNow()), false);
                }

                if (tick % 30 == 0)
                {
                    if (this.reconciling.IsCompleted)
                    {
                        this.reconciling = this.Reconcile(cancellationToken);
                    }

                    foreach (var symbol in Volatile.Read(ref this.owned))
                    {
                        var quote = events.LatestBook(symbol);
                        if (
                            !events.Snapshot().Connected
                            || quote is null
                            || time.GetUtcNow() - quote.ObservedAt > TimeSpan.FromSeconds(30)
                        )
                        {
                            this.RefreshQuote(symbol, cancellationToken);
                        }
                    }
                }

                if (monitor.Version != version)
                {
                    version = monitor.Version;
                    if (monitor.LatestClosedCandle is { } candle)
                    {
                        events.Enqueue(
                            new(TradingEventKind.CandleClosed, "", 0, time.GetUtcNow(), candleAt: candle),
                            false
                        );
                    }
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed(logger, null);
            }

            tick++;
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task Reconcile(CancellationToken cancellationToken)
    {
        try
        {
            await engine.PrepareReconciliation(cancellationToken);
            events.EnqueueMaintenance(true);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            failed(logger, null);
        }
    }

    private void RefreshQuote(string symbol, CancellationToken cancellationToken)
    {
        lock (this.quoteGate)
        {
            if (
                symbol.Length == 0
                || (this.quoteReads.TryGetValue(symbol, out var at) && time.GetUtcNow() - at < TimeSpan.FromSeconds(1))
                || this.quoteTasks.TryGetValue(symbol, out var existing) && !existing.IsCompleted
            )
            {
                return;
            }

            this.quoteReads[symbol] = time.GetUtcNow();
            this.quoteTasks[symbol] = this.ReadQuote(symbol, cancellationToken);
            foreach (
                var removed in this
                    .quoteTasks.Where(pair =>
                        pair.Value.IsCompleted
                        && !Volatile.Read(ref this.owned).Contains(pair.Key, StringComparer.Ordinal)
                    )
                    .Select(pair => pair.Key)
                    .ToArray()
            )
            {
                this.quoteTasks.TryRemove(removed, out _);
                this.quoteReads.TryRemove(removed, out _);
            }
        }
    }

    private async Task ReadQuote(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var book in await markets.ReadValuations([symbol], cancellationToken))
            {
                if (book.Symbol != symbol)
                {
                    throw new InvalidDataException("Unexpected fallback quote identity.");
                }

                events.Enqueue(
                    new(TradingEventKind.BookChanged, symbol, 0, book.ObservedAt, book: book),
                    true,
                    started
                );
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            failed(logger, null);
        }
    }

    private async Task Deliver(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await reports.Tick(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed(logger, null);
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
}
