namespace Quantara.MarketData.Infrastructure.News;

public sealed record NewsTransportResponse(
    int StatusCode,
    string? Body,
    string? ETag,
    string? LastModified,
    string? RetryAfter
);
