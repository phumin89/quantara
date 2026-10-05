using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;

namespace TradingCompany.LiveExecution;

public sealed class LiveReconciliationWorker(LiveExecutionService? execution, LiveReconciliation? reconciliation,
    LiveAccountId accountId, TimeProvider clock, LiveTradingStore? durableStore = null) : BackgroundService
{
    private bool recovered;
    public string Status { get; private set; } = "DISABLED";
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (execution is null || reconciliation is null || accountId.Value == Guid.Empty) return;
        try
        {
            if (durableStore is not null && !await durableStore.ExistsAsync(accountId, cancellationToken))
            {
                Status = "ACCOUNT_NOT_PROVISIONED";
                await base.StartAsync(cancellationToken);
                return;
            }
            // Recovery commits before the host starts accepting owner/advisory traffic.
            // It preserves Stop and never assumes an ambiguous order did not execute.
            await execution.SuspendForRecoveryAsync(accountId, cancellationToken);
            recovered = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            Status = "RECOVERY_FAILED";
            throw new InvalidOperationException("Live recovery could not be persisted. Executor startup is blocked.");
        }
        Status = "RECOVERING";
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        try
        {
            do
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(25));
                    if (!recovered)
                    {
                        if (!await durableStore!.ExistsAsync(accountId, deadline.Token)) continue;
                        await execution!.SuspendForRecoveryAsync(accountId, deadline.Token);
                        recovered = true;
                    }
                    var result = await reconciliation!.ReconcileAsync(accountId, deadline.Token);
                    Status = result.Complete ? "RECONCILED" : "RECONCILIATION_BLOCKED";
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { Status = "RECONCILIATION_UNAVAILABLE"; }
                // Read/reconcile may repeat; placement/cancellation never runs in this worker.
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
