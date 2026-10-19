namespace Quantara.MarketData.Domain.News;

public sealed record NewsEvent(
    string EventId,
    int Revision,
    string SourceId,
    string Title,
    string Url,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset RevisionSeenAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Symbols,
    bool MarketWide,
    string Category,
    string FactStatus,
    string ContentHash
);
