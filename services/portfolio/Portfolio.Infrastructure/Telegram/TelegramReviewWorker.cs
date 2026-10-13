using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TelegramReviewWorker(
    TelegramOrderReviews reviews,
    IOrderNotificationStore notifications,
    ITelegramTransport transport,
    ILogger<TelegramReviewWorker> logger
) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> notificationFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "TelegramNotificationUnavailable"),
        "Telegram notification unavailable; uncertain delivery is retained without resend."
    );

    private static readonly Action<ILogger, Exception?> pollingFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, "TelegramPollingUnavailable"),
        "Telegram review polling unavailable; no order is automatically retried."
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reviews.Notify(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TelegramReviewWorker.notificationFailed(logger, null);
            }

            try
            {
                var clicks = await transport.Poll(await notifications.Cursor(stoppingToken), stoppingToken);
                foreach (var click in clicks)
                {
                    await reviews.Process(click, stoppingToken);
                    await notifications.Advance(click.UpdateId + 1, stoppingToken);
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TelegramReviewWorker.pollingFailed(logger, null);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
