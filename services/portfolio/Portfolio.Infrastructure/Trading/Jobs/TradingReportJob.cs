using Hangfire;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed class TradingReportJob(
    IAccountContext accountContext,
    TradingReportDelivery delivery,
    TradingJobOptions options
)
{
    [Queue("maintenance")]
    [AutomaticRetry(Attempts = 0)]
    public Task Prepare(string accountKey, string destinationKey, CancellationToken cancellationToken)
    {
        if (
            !options.UseHangfire
            || !string.Equals(accountContext.AccountKey, accountKey, StringComparison.Ordinal)
            || !string.Equals(options.AccountKey, accountKey, StringComparison.Ordinal)
            || !string.Equals(options.DestinationKey, destinationKey, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException("Trading report job account mismatch.");
        }

        return delivery.PrepareReports(cancellationToken);
    }
}
