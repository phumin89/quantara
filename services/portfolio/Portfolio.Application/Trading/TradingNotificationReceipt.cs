namespace Quantara.Portfolio.Application;

public sealed record TradingNotificationReceipt
{
    public TradingNotificationReceipt(string key, DateTimeOffset claimedAt, long messageId, DateTimeOffset sentAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 200 || key != key.Trim() || messageId <= 0 || claimedAt == default || sentAt < claimedAt)
        {
            throw new ArgumentException("A notification receipt requires an exact claim and positive acknowledgement.");
        }

        this.Key = key;
        this.ClaimedAt = claimedAt;
        this.MessageId = messageId;
        this.SentAt = sentAt;
    }

    public string Key { get; }

    public DateTimeOffset ClaimedAt { get; }

    public long MessageId { get; }

    public DateTimeOffset SentAt { get; }
}
