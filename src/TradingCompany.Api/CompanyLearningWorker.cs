using TradingCompany.AgentRuntime;

namespace TradingCompany.Api;

public sealed class CompanyLearningWorker(CompanyLearningService learning, TimeProvider clock,
    ILogger<CompanyLearningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), clock);
        do
        {
            try { await learning.RefreshAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("Daily learning unavailable; current market checks continue."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
