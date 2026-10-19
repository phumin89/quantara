using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed class TradingNotificationScheduler(
    ITradingStore store,
    IBackgroundJobClient jobs,
    JobStorage storage,
    SqlTradingJobDispatchStore dispatch,
    TradingJobOptions options,
    TimeProvider time,
    TradingNotificationDiagnostics diagnostics,
    ILogger<TradingNotificationScheduler> logger
) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "NotificationQueueUnavailable"),
        "Notification scheduling unavailable; canonical alerts and uncertain claims are retained."
    );

    private int offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Enabled && options.UseHangfire)
                {
                    await this.SchedulePending(stoppingToken);
                }

                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failures = Math.Min(6, failures + 1);
                TradingNotificationScheduler.failed(logger, null);
            }

            await Task.Delay(TimeSpan.FromSeconds(failures == 0 ? 2 : Math.Min(60, 1 << failures)), stoppingToken);
        }
    }

    private async Task SchedulePending(CancellationToken cancellationToken)
    {
        var state = await store.Read(cancellationToken);
        var scheduled = 0;
        using var connection = storage.GetConnection();
        var pending = state
            .Alerts.Where(item =>
                item.Status == "pending"
                && (item.RetryAt is null || item.RetryAt <= time.GetUtcNow())
                && (state.Rules.Adaptive is null || item.Kind == "fill")
            )
            .OrderBy(item => item.CreatedAt)
            .ToArray();
        var start = pending.Length == 0 ? 0 : this.offset % pending.Length;
        for (var index = 0; index < Math.Min(64, pending.Length); index++)
        {
            var alert = pending[(start + index) % pending.Length];
            this.offset = (start + index + 1) % pending.Length;
            var previous = await dispatch.Read(alert.Key, cancellationToken);
            if (
                previous is { JobId: { } previousId }
                && connection.GetJobData(previousId)
                    is { State: "Enqueued" or "Processing" or "Scheduled" or "Awaiting" }
            )
            {
                continue;
            }

            var lease = await dispatch.Claim(alert.Key, options.DestinationKey, time.GetUtcNow(), cancellationToken);
            if (lease is null)
            {
                continue;
            }

            var jobId = jobs.Enqueue<TradingNotificationJob>(job =>
                job.Deliver(options.AccountKey, options.DestinationKey, alert.Key, CancellationToken.None)
            );
            await dispatch.MarkEnqueued(alert.Key, lease.Value, jobId, cancellationToken);
            if (++scheduled == 4)
            {
                break;
            }
        }

        diagnostics.SetBlocked(await dispatch.CountBlocked(options.DestinationKey, cancellationToken));
    }
}
