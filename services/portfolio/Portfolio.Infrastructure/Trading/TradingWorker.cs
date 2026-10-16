using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class TradingWorker(TradingEngine engine, TradingReportDelivery reports, ILogger<TradingWorker> logger)
    : BackgroundService
{
    private static readonly Action<ILogger, Exception?> engineFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "TradingTickUnavailable"),
        "Trading tick unavailable; persisted claims and uncertain outcomes are retained."
    );

    private static readonly Action<ILogger, Exception?> deliveryFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, "TradingReportUnavailable"),
        "Trading report delivery unavailable; uncertain notifications are not resent."
    );

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(this.RunEngine(stoppingToken), this.RunReports(stoppingToken));

    private async Task RunEngine(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await engine.Tick(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TradingWorker.engineFailed(logger, null);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task RunReports(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reports.Tick(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                TradingWorker.deliveryFailed(logger, null);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
