namespace Quantara.MarketData.Domain.News;

public sealed record NewsSourceState(
    string Id,
    string Label,
    string Url,
    string Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextRetryAt,
    string? Reason
);
