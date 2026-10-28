using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingPolicyWorker(
    TradingPolicyService policy,
    TimeProvider time,
    ILogger<TradingPolicyWorker> logger
) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(3, "PolicyReviewUnavailable"),
        "AI policy review unavailable; retained trading settings remain unchanged."
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        do
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                await policy.Review(timeout.Token);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TradingPolicyWorker.failed(logger, null);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
