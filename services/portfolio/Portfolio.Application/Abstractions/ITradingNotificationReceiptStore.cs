namespace Quantara.Portfolio.Application;

public interface ITradingNotificationReceiptStore
{
    public Task Save(TradingNotificationReceipt receipt, CancellationToken cancellationToken);

    public Task<TradingNotificationReceipt?> Read(
        string key,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken
    );
}
