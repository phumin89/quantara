namespace Quantara.Portfolio.Domain.Trading;

public enum TradingEventKind
{
    QuoteChanged,
    BookChanged,
    CandleClosed,
    FeedUnavailable,
    Heartbeat,
    Maintenance,
    Reconcile,
    Research,
}
