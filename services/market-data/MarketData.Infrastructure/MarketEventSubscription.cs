using System.Threading.Channels;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class MarketEventSubscription
{
    private readonly object gate = new();

    private readonly Dictionary<(string Symbol, MarketEventKind Kind), MarketEvent> pending = [];

    private readonly HashSet<string> dirty = new(StringComparer.Ordinal);

    private readonly Channel<bool> signal = Channel.CreateBounded<bool>(1);

    private long coalesced;

    public void Publish(MarketEvent item)
    {
        lock (this.gate)
        {
            var key = (item.Symbol, item.Kind);
            if (this.pending.ContainsKey(key))
            {
                this.coalesced++;
            }
            else if (this.pending.Count >= 256)
            {
                var removed = this.pending.Keys.First();
                this.pending.Remove(removed);
                if (removed.Symbol.Length > 0 && this.dirty.Count < 64)
                {
                    this.dirty.Add(removed.Symbol);
                }

                this.coalesced++;
            }

            this.pending[key] = item;
            this.signal.Writer.TryWrite(true);
        }
    }

    public async Task<MarketEvent> Read(
        Func<string, long> nextSequence,
        string[] watched,
        TimeProvider clock,
        TimeSpan heartbeat,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            lock (this.gate)
            {
                if (this.dirty.Count > 0)
                {
                    var symbol = this.dirty.First();
                    this.dirty.Remove(symbol);
                    return new(
                        MarketEventKind.BookChanged,
                        symbol,
                        nextSequence(symbol),
                        clock.GetUtcNow(),
                        null,
                        null,
                        watched,
                        true,
                        this.coalesced
                    );
                }

                if (this.pending.Count > 0)
                {
                    var key = this.pending.Keys.First();
                    var item = this.pending[key];
                    this.pending.Remove(key);
                    return item with { WatchedSymbols = watched, Coalesced = this.coalesced };
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(heartbeat);
            try
            {
                await this.signal.Reader.ReadAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(
                    MarketEventKind.Heartbeat,
                    "",
                    0,
                    clock.GetUtcNow(),
                    null,
                    null,
                    watched,
                    false,
                    this.coalesced
                );
            }
        }
    }
}
