using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public sealed class FakeOrderNotificationStore : IOrderNotificationStore
{
    private readonly object gate = new();

    private readonly Dictionary<string, OrderNotification> notifications = [];

    private long cursor;

    public bool FailFind { get; set; }

    public bool FailClaimReview { get; set; }

    public bool FailClaimReviewAfterWrite { get; set; }

    public bool FailAdvance { get; set; }

    public bool FailAdvanceAfterWrite { get; set; }

    public Task<OrderNotification?> Reserve(Guid orderId, long userId, long chatId, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            if (this.notifications.Values.Any(item => item.OrderId == orderId))
            {
                return Task.FromResult<OrderNotification?>(null);
            }

            var value = new OrderNotification(
                orderId,
                Guid.NewGuid().ToString("N"),
                userId,
                chatId,
                null,
                OrderNotificationState.Sending
            );
            this.notifications.Add(value.Token, value);
            return Task.FromResult<OrderNotification?>(value);
        }
    }

    public Task Sent(string token, long messageId, CancellationToken cancellationToken)
    {
        lock (this.gate)
        {
            this.notifications[token] = this.notifications[token] with
            {
                MessageId = messageId,
                State = OrderNotificationState.Sent,
            };
        }

        return Task.CompletedTask;
    }

    public Task<OrderNotification?> Find(string token, CancellationToken cancellationToken)
    {
        if (this.FailFind)
        {
            throw new InvalidOperationException("Notification lookup unavailable fixture");
        }

        lock (this.gate)
        {
            return Task.FromResult(this.notifications.GetValueOrDefault(token));
        }
    }

    public Task<bool> ClaimReview(string token, string action, CancellationToken cancellationToken)
    {
        if (this.FailClaimReview)
        {
            throw new InvalidOperationException("Review claim unavailable fixture");
        }

        lock (this.gate)
        {
            var current = this.notifications[token];
            if (current.State != OrderNotificationState.Sent)
            {
                return Task.FromResult(false);
            }

            this.notifications[token] = current with { State = OrderNotificationState.Reviewed };
            if (this.FailClaimReviewAfterWrite)
            {
                throw new InvalidOperationException("Review claim acknowledgement unavailable fixture");
            }

            return Task.FromResult(true);
        }
    }

    public Task<long> Cursor(CancellationToken cancellationToken) => Task.FromResult(this.cursor);

    public Task Advance(long offset, CancellationToken cancellationToken)
    {
        if (this.FailAdvance)
        {
            throw new InvalidOperationException("Cursor unavailable fixture");
        }

        this.cursor = Math.Max(this.cursor, offset);
        if (this.FailAdvanceAfterWrite)
        {
            throw new InvalidOperationException("Cursor acknowledgement unavailable fixture");
        }

        return Task.CompletedTask;
    }
}
