namespace Quantara.MarketData.Domain;

public enum MarketEventKind
{
    QuoteChanged,
    BookChanged,
    CandleClosed,
    FeedUnavailable,
    Heartbeat,
}
