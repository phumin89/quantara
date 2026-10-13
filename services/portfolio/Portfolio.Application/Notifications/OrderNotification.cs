using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record OrderNotification(
    Guid OrderId,
    string Token,
    long UserId,
    long ChatId,
    long? MessageId,
    OrderNotificationState State
);
