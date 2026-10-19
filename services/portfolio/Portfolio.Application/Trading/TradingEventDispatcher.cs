using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class TradingEventDispatcher(int capacity = 128) : IDisposable
{
    private readonly object gate = new();

    private readonly int limit = Math.Clamp(capacity, 1, 1024);

    private readonly List<(TradingMarketEvent Event, bool Owned)> items = [];

    private readonly Dictionary<string, TradingBook> latest = new(StringComparer.Ordinal);

    private readonly Dictionary<string, long> received = new(StringComparer.Ordinal);

    private readonly HashSet<string> dirty = new(StringComparer.Ordinal);

    private readonly Dictionary<TradingEventKind, TradingMarketEvent> controls = [];

    private readonly Queue<double> latencies = new();

    private readonly SemaphoreSlim signal = new(0, 1);

    private bool connected;

    private DateTimeOffset? lastEventAt;

    private DateTimeOffset? lastExitAt;

    private DateTimeOffset? lastReconcileAt;

    private string[] watched = [];

    private long coalesced;

    private long dropped;

    private long transportCoalesced;

    private double? submitLatency;

    private int exitBurst;

    public void Enqueue(TradingMarketEvent item, bool owned, long? snapshotStarted = null)
    {
        lock (this.gate)
        {
            if (snapshotStarted is { } started && this.received.GetValueOrDefault(item.Symbol) > started)
            {
                this.coalesced++;
                return;
            }

            this.ObserveCore(item);

            if (item.Kind is TradingEventKind.Maintenance or TradingEventKind.Reconcile or TradingEventKind.Research)
            {
                if (this.controls.ContainsKey(item.Kind))
                {
                    this.coalesced++;
                }

                this.controls[item.Kind] = item;
                this.Wake();
                return;
            }

            var index = this.items.FindIndex(value =>
                value.Event.Symbol == item.Symbol
                && value.Event.Kind == item.Kind
                && (!owned || value.Event.Sequence == item.Sequence)
            );
            if (index >= 0)
            {
                this.items[index] = (item, owned || this.items[index].Owned);
                this.coalesced++;
            }
            else if (this.items.Count < this.limit)
            {
                this.items.Add((item, owned));
            }
            else
            {
                var removable = this.items.FindIndex(value =>
                    !value.Owned && value.Event.Kind != TradingEventKind.Reconcile
                );
                if (removable >= 0 && owned)
                {
                    this.items.RemoveAt(removable);
                    this.items.Add((item, true));
                }
                else if (owned && item.Symbol.Length > 0 && this.dirty.Count < 64)
                {
                    this.dirty.Add(item.Symbol);
                }

                this.dropped++;
            }

            if (item.Dirty && owned && item.Symbol.Length > 0 && this.dirty.Count < 64)
            {
                this.dirty.Add(item.Symbol);
            }

            this.Wake();
        }
    }

    public void Observe(TradingMarketEvent item)
    {
        lock (this.gate)
        {
            this.ObserveCore(item);
        }
    }

    public void EnqueueMaintenance(bool reconciliation = false) =>
        this.Enqueue(
            new(
                reconciliation ? TradingEventKind.Reconcile : TradingEventKind.Maintenance,
                "",
                0,
                DateTimeOffset.UtcNow
            ),
            false
        );

    public async ValueTask<TradingMarketEvent?> Dequeue(CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (this.gate)
            {
                if (this.exitBurst >= 16 && this.controls.Remove(TradingEventKind.Reconcile, out var due))
                {
                    this.exitBurst = 0;
                    return due;
                }

                if (this.dirty.Count > 0)
                {
                    this.exitBurst++;
                    var symbol = this.dirty.First();
                    this.dirty.Remove(symbol);
                    return new(
                        TradingEventKind.BookChanged,
                        symbol,
                        0,
                        DateTimeOffset.UtcNow,
                        book: this.latest.GetValueOrDefault(symbol),
                        dirty: true
                    );
                }

                var ownedExit = this.items.Any(value => Priority(value.Event.Kind, value.Owned) == 4);
                if (!ownedExit)
                {
                    foreach (var kind in new[] { TradingEventKind.Reconcile, TradingEventKind.Maintenance })
                    {
                        if (this.controls.Remove(kind, out var control))
                        {
                            this.exitBurst = 0;
                            return control;
                        }
                    }
                }

                if (this.items.Count > 0)
                {
                    var index = this
                        .items.Select(
                            (value, index) => (Index: index, Priority: Priority(value.Event.Kind, value.Owned))
                        )
                        .MaxBy(value => value.Priority)
                        .Index;
                    var item = this.items[index].Event;
                    this.exitBurst = Priority(item.Kind, this.items[index].Owned) == 4 ? this.exitBurst + 1 : 0;
                    this.items.RemoveAt(index);
                    return item;
                }

                if (this.controls.Remove(TradingEventKind.Research, out var research))
                {
                    return research;
                }
            }

            await this.signal.WaitAsync(cancellationToken);
        }
    }

    public TradingBook? LatestBook(string symbol)
    {
        lock (this.gate)
        {
            return this.latest.GetValueOrDefault(symbol);
        }
    }

    public IReadOnlyList<TradingBook> LatestBooks()
    {
        lock (this.gate)
        {
            return this.latest.Values.ToArray();
        }
    }

    public void SetFeed(bool connected, DateTimeOffset now, IReadOnlyList<string> watched)
    {
        lock (this.gate)
        {
            this.connected = connected;
            if (!connected)
            {
                this.transportCoalesced = 0;
                this.latest.Clear();
                this.received.Clear();
            }

            if (connected)
            {
                this.lastEventAt = now;
            }

            this.watched = watched.Take(64).ToArray();
            foreach (
                var symbol in this
                    .received.Keys.Where(symbol => !this.watched.Contains(symbol, StringComparer.Ordinal))
                    .ToArray()
            )
            {
                this.latest.Remove(symbol);
                this.received.Remove(symbol);
            }
        }
    }

    public void RecordDecision(TimeSpan latency, DateTimeOffset at, bool exit)
    {
        lock (this.gate)
        {
            if (exit)
            {
                this.lastExitAt = at;
            }

            this.latencies.Enqueue(Math.Max(0, latency.TotalMilliseconds));
            while (this.latencies.Count > 512)
            {
                this.latencies.Dequeue();
            }
        }
    }

    public void RecordReconciliation(DateTimeOffset at)
    {
        lock (this.gate)
        {
            this.lastReconcileAt = at;
        }
    }

    public void RecordSubmission(TimeSpan latency)
    {
        lock (this.gate)
        {
            this.submitLatency = Math.Max(0, latency.TotalMilliseconds);
        }
    }

    public TradingStreamStatus Snapshot()
    {
        lock (this.gate)
        {
            var samples = this.latencies.Order().ToArray();
            return new(
                this.connected,
                this.lastEventAt,
                this.watched,
                this.dirty.ToArray(),
                this.coalesced,
                this.dropped,
                Percentile(samples, .5),
                Percentile(samples, .95),
                this.lastExitAt,
                this.lastReconcileAt,
                this.submitLatency
            );
        }
    }

    public void Dispose() => this.signal.Dispose();

    private void ObserveCore(TradingMarketEvent item)
    {
        if (item.Coalesced > this.transportCoalesced)
        {
            this.coalesced += item.Coalesced - this.transportCoalesced;
            this.transportCoalesced = item.Coalesced;
        }

        if (item.Kind == TradingEventKind.FeedUnavailable || item.Dirty)
        {
            this.latest.Remove(item.Symbol);
            if (item.Symbol.Length > 0 && (this.received.Count < 64 || this.received.ContainsKey(item.Symbol)))
            {
                this.received[item.Symbol] = item.GetReceivedTimestamp();
            }
        }
        else if (
            item.Book is { } book
            && (this.latest.Count < 64 || this.latest.ContainsKey(item.Symbol))
            && (!this.latest.TryGetValue(item.Symbol, out var previous) || book.ObservedAt >= previous.ObservedAt)
        )
        {
            this.latest[item.Symbol] = book;
            this.received[item.Symbol] = item.GetReceivedTimestamp();
        }
    }

    private void Wake()
    {
        if (this.signal.CurrentCount == 0)
        {
            this.signal.Release();
        }
    }

    private static int Priority(TradingEventKind kind, bool owned) =>
        owned
        && kind is TradingEventKind.QuoteChanged or TradingEventKind.BookChanged or TradingEventKind.FeedUnavailable
            ? 4
        : kind == TradingEventKind.Reconcile ? 3
        : kind == TradingEventKind.CandleClosed ? 2
        : 1;

    private static double? Percentile(double[] values, double percentile) =>
        values.Length == 0
            ? null
            : values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];
}
