namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingAlert(
    string Key,
    string Kind,
    string Text,
    DateTimeOffset CreatedAt,
    string Status = "pending"
)
{
    public DateTimeOffset? ClaimedAt { get; init; }

    public long? MessageId { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    public DateTimeOffset? RetryAt { get; init; }

    public int RetryCount { get; init; }
}
