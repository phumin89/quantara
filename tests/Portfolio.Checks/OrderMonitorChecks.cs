using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderMonitorChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        foreach (var count in new[] { 6, 57 })
        {
            var states = new[]
            {
                OrderState.Submitted,
                OrderState.PartiallyFilled,
                OrderState.Unknown,
                OrderState.Submitting,
            };
            var proposals = Enumerable
                .Range(0, count)
                .Select(index => new OrderProposal(
                    Guid.NewGuid(),
                    "ETH",
                    TradeSide.Buy,
                    100m,
                    1000m,
                    1000m,
                    "Monitor fixture",
                    now.AddMinutes(index - 100),
                    now.AddHours(1),
                    states[index % states.Length],
                    "fixture",
                    null
                ))
                .ToArray();
            var store = new OrderMonitorStore(proposals);
            var mediator = new OrderMonitorMediator();
            var calls = new List<Guid>();
            var fail = false;
            mediator.Reconcile = (command, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls.Add(command.Id);
                if (fail)
                {
                    throw new HttpRequestException("Fixture outage");
                }

                return OrderView.From(store.Proposals.Single(item => item.Id == command.Id), now);
            };
            await using var provider = new ServiceCollection().AddSingleton<IMediator>(mediator).BuildServiceProvider();
            using var monitor = new OrderStatusMonitor(store, provider.GetRequiredService<IServiceScopeFactory>());
            foreach (var failing in new[] { false, true })
            {
                fail = failing;
                calls.Clear();
                for (var cycle = 0; cycle < (count + 4) / 5; cycle++)
                {
                    var before = calls.Count;
                    await monitor.Poll(CancellationToken.None);
                    Check(calls.Count - before == 5, "Each cycle attempts at most five status reads.");
                }

                Check(
                    calls.Distinct().Count() == count,
                    "Every active order is visited, beyond both the first five and the UI list cap, even after failures."
                );
            }

            store.Proposals =
            [
                proposals[^1],
                proposals[0] with
                {
                    State = OrderState.Filled,
                },
                proposals[1] with
                {
                    State = OrderState.Pending,
                },
            ];
            calls.Clear();
            fail = false;
            await monitor.Poll(CancellationToken.None);
            Check(calls.SequenceEqual([proposals[^1].Id]), "Membership changes retain only currently eligible orders.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await monitor.Poll(cancelled.Token);
                throw new InvalidOperationException("Monitor ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
                Check(calls.Count == 1, "Cancellation prevents any new status read.");
            }
        }

        Console.WriteLine(
            "Order monitor: bounded fair polling, failures, list caps and cancellation passed; no broker calls."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
