using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeTradingNotificationReceiptStore : ITradingNotificationReceiptStore
{
    private readonly Dictionary<(string Key, DateTimeOffset Claim), TradingNotificationReceipt> receipts = [];

    public IReadOnlyCollection<TradingNotificationReceipt> Receipts => this.receipts.Values;

    public int SaveAttempts { get; private set; }

    public int Reads { get; private set; }

    public HashSet<string> ReadKeys { get; } = new(StringComparer.Ordinal);

    public int FailWrites { get; set; }

    public int FailAfterSave { get; set; }

    public TradingNotificationReceipt? ReadOverride { get; set; }

    public Task Save(TradingNotificationReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.SaveAttempts++;
        if (this.FailWrites > 0)
        {
            this.FailWrites--;
            throw new IOException("Receipt persistence fixture is unavailable.");
        }

        var identity = (receipt.Key, receipt.ClaimedAt);
        if (this.receipts.TryGetValue(identity, out var existing) && existing != receipt)
        {
            throw new InvalidOperationException("Receipt acknowledgement conflicts.");
        }

        this.receipts[identity] = receipt;
        if (this.FailAfterSave > 0)
        {
            this.FailAfterSave--;
            throw new IOException("Receipt write acknowledgement fixture is unavailable.");
        }

        return Task.CompletedTask;
    }

    public Task<TradingNotificationReceipt?> Read(
        string key,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken
    )
    {
        this.Reads++;
        this.ReadKeys.Add(key);
        return Task.FromResult(this.ReadOverride ?? this.receipts.GetValueOrDefault((key, claimedAt)));
    }
}
