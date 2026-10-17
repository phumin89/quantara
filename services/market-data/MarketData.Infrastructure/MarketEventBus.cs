using System.Collections.Concurrent;
using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Infrastructure;

public sealed class MarketEventBus(TimeProvider? clock = null, TimeSpan? heartbeat = null) : IMarketEventBus
{
    private readonly ConcurrentDictionary<Guid, MarketEventSubscription> subscribers = new();

    private readonly ConcurrentDictionary<string, long> sequences = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, long> closedCandles = new(StringComparer.Ordinal);

    private string[] watchedSymbols = [];

    private string[] requiredSymbols = [];

    private int subscriberCount;

    public IReadOnlyList<string> WatchedSymbols => Volatile.Read(ref this.watchedSymbols).ToArray();

    public IReadOnlyList<string> RequiredSymbols => Volatile.Read(ref this.requiredSymbols).ToArray();

    public ValueTask SetWatch(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        Validate(symbols);
        Volatile.Write(ref this.requiredSymbols, symbols.Order(StringComparer.Ordinal).ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask SetWatched(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        Validate(symbols);
        Volatile.Write(ref this.watchedSymbols, symbols.Order(StringComparer.Ordinal).ToArray());
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<MarketEvent> Read(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (Interlocked.Increment(ref this.subscriberCount) > 8)
        {
            Interlocked.Decrement(ref this.subscriberCount);
            throw new InvalidOperationException("The private market stream subscriber limit was reached.");
        }

        var id = Guid.NewGuid();
        var subscription = new MarketEventSubscription();
        this.subscribers[id] = subscription;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var item = await subscription.Read(
                    this.NextSequence,
                    Volatile.Read(ref this.watchedSymbols),
                    clock ?? TimeProvider.System,
                    heartbeat ?? TimeSpan.FromSeconds(15),
                    cancellationToken
                );
                yield return item with
                {
                    WatchedSymbols = Volatile.Read(ref this.watchedSymbols),
                };
            }
        }
        finally
        {
            this.subscribers.TryRemove(id, out _);
            Interlocked.Decrement(ref this.subscriberCount);
        }
    }

    public bool Publish(
        MarketEventKind kind,
        string symbol,
        DateTimeOffset observedAt,
        MarketEventBook? book = null,
        long? candleAt = null
    )
    {
        if (
            kind is < MarketEventKind.QuoteChanged or > MarketEventKind.FeedUnavailable
            || symbol.Length is < 1 or > 30
            || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
        )
        {
            return false;
        }

        if (kind == MarketEventKind.CandleClosed)
        {
            if (
                candleAt is not { } candle
                || candle % 300 != 0
                || candle > observedAt.ToUnixTimeSeconds() / 300 * 300 - 300
            )
            {
                return false;
            }

            while (true)
            {
                var previous = this.closedCandles.GetOrAdd(symbol, long.MinValue);
                if (candle <= previous)
                {
                    return false;
                }

                if (this.closedCandles.TryUpdate(symbol, candle, previous))
                {
                    break;
                }
            }
        }

        var item = new MarketEvent(
            kind,
            symbol,
            this.NextSequence(symbol),
            observedAt,
            candleAt,
            book,
            Volatile.Read(ref this.watchedSymbols),
            false,
            0
        );
        foreach (var subscription in this.subscribers.Values)
        {
            subscription.Publish(item);
        }

        return true;
    }

    private long NextSequence(string symbol) => this.sequences.AddOrUpdate(symbol, 1, (_, previous) => previous + 1);

    private static void Validate(IReadOnlyList<string> symbols)
    {
        if (
            symbols.Count > 64
            || symbols.Any(symbol =>
                symbol.Length is < 1 or > 30
                || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
            || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Count
        )
        {
            throw new ArgumentException("Provide at most 64 unique uppercase ASCII base symbols.", nameof(symbols));
        }
    }
}
