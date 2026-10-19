namespace Quantara.Portfolio.Application;

public sealed class TradingNotificationRejectedException(int code, int? retryAfterSeconds = null)
    : Exception("Telegram explicitly rejected the notification without accepting it.")
{
    public int Code { get; } = code;

    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
