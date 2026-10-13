using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class OrderStatusMonitor(IReadOnlyOrderStore store, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var proposals = await store.List(stoppingToken);
                foreach (
                    var proposal in proposals
                        .Where(item =>
                            item.State
                                is OrderState.Submitted
                                    or OrderState.PartiallyFilled
                                    or OrderState.Unknown
                                    or OrderState.Submitting
                        )
                        .Take(5)
                )
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope
                            .ServiceProvider.GetRequiredService<IMediator>()
                            .Send(new ReconcileOrderCommand(proposal.Id), stoppingToken);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Read failures retain the last verified state and never trigger submission.
                    }
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // Storage outages cannot authorize retrying an order.
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
