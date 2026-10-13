namespace Quantara.Portfolio.Infrastructure;

public sealed record TelegramClick(
    long UpdateId,
    string CallbackId,
    long UserId,
    long ChatId,
    long MessageId,
    string Data
);
