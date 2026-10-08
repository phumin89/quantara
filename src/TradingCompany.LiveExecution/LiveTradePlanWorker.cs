using TradingCompany.Application;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed class LiveTradePlanWorker(LiveTradePlanService plans, LiveTradingStore store,
    BitkubConfiguredAccount configured, TimeProvider clock, ILogger<LiveTradePlanWorker>? logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    if (await store.ExistsAsync(configured.AccountId, deadline.Token))
                        await plans.TickAsync(configured.AccountId, deadline.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { logger?.LogWarning("TRADE_PLAN_MONITOR_UNAVAILABLE"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
