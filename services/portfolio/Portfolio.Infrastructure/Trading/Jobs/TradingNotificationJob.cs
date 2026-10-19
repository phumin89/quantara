using Hangfire;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed class TradingNotificationJob(
    IAccountContext accountContext,
    TradingReportDelivery delivery,
    TradingJobOptions options
)
{
    [Queue("telegram")]
    [AutomaticRetry(Attempts = 0)]
    public Task Deliver(string accountKey, string destinationKey, string alertKey, CancellationToken cancellationToken)
    {
        if (
            !options.Enabled
            || !options.UseHangfire
            || !string.Equals(accountContext.AccountKey, accountKey, StringComparison.Ordinal)
            || !string.Equals(options.AccountKey, accountKey, StringComparison.Ordinal)
            || !string.Equals(options.DestinationKey, destinationKey, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException("Trading notification job account or destination mismatch.");
        }

        return delivery.DeliverAlert(alertKey, cancellationToken);
    }
}
