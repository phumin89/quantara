namespace Quantara.MarketData.Infrastructure.News;

public sealed record NewsSourceValidator(string? ETag, string? LastModified, DateTimeOffset? NextRetryAt);
