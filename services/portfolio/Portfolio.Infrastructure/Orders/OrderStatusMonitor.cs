using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class OrderStatusMonitor(
    IReadOnlyOrderStore store,
    IServiceScopeFactory scopes,
    TimeProvider? time = null
) : BackgroundService
{
    private int cursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.Poll(stoppingToken);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Storage outages cannot authorize retrying an order.
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    public async Task Poll(CancellationToken cancellationToken)
    {
        var proposals = (await store.Exposure((time ?? TimeProvider.System).GetUtcNow(), cancellationToken))
            .Where(item =>
                item.State
                    is OrderState.Submitted
                        or OrderState.PartiallyFilled
                        or OrderState.Unknown
                        or OrderState.Submitting
            )
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArray();
        for (var index = 0; index < Math.Min(5, proposals.Length); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proposal = proposals[this.cursor % proposals.Length];
            this.cursor = (this.cursor + 1) % proposals.Length;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope
                    .ServiceProvider.GetRequiredService<IMediator>()
                    .Send(new ReconcileOrderCommand(proposal.Id), cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Read failures retain the last verified state and never trigger submission.
            }
        }
    }
}
