using System.Diagnostics;

namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingMarketEvent
{
    private long receivedTimestamp = Stopwatch.GetTimestamp();

    public TradingEventKind Kind { get; }

    public string Symbol { get; }

    public long Sequence { get; }

    public DateTimeOffset ObservedAt { get; }

    public long? CandleAt { get; }

    public TradingBook? Book { get; }

    public IReadOnlyList<string> WatchedSymbols { get; }

    public bool Dirty { get; }

    public long Coalesced { get; }

    public long GetReceivedTimestamp() => this.receivedTimestamp;

    public TradingMarketEvent(
        TradingEventKind kind,
        string symbol,
        long sequence,
        DateTimeOffset observedAt,
        long? candleAt = null,
        TradingBook? book = null,
        IReadOnlyList<string>? watchedSymbols = null,
        bool dirty = false,
        long coalesced = 0
    )
    {
        this.Kind = kind;
        this.Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        this.Sequence = sequence;
        this.ObservedAt = observedAt;
        this.CandleAt = candleAt;
        this.Book = book;
        this.WatchedSymbols = watchedSymbols ?? Array.Empty<string>();
        this.Dirty = dirty;
        this.Coalesced = coalesced;
    }

    public TradingMarketEvent WithCoalesced(long value) =>
        new(
            this.Kind,
            this.Symbol,
            this.Sequence,
            this.ObservedAt,
            this.CandleAt,
            this.Book,
            this.WatchedSymbols,
            this.Dirty,
            value
        )
        {
            receivedTimestamp = this.receivedTimestamp,
        };
}
