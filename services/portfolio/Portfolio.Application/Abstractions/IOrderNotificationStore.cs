namespace Quantara.Portfolio.Application;

public interface IOrderNotificationStore
{
    public Task<OrderNotification?> Reserve(
        Guid orderId,
        long userId,
        long chatId,
        CancellationToken cancellationToken
    );

    public Task Sent(string token, long messageId, CancellationToken cancellationToken);

    public Task<OrderNotification?> Find(string token, CancellationToken cancellationToken);

    public Task<bool> ClaimReview(string token, string action, CancellationToken cancellationToken);

    public Task<long> Cursor(CancellationToken cancellationToken);

    public Task Advance(long offset, CancellationToken cancellationToken);
}
