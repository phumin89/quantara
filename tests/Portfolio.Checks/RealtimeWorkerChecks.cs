using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class RealtimeWorkerChecks
{
    public static async Task Run()
    {
        using var fixture = new AutonomousFixture();
        using var events = new TradingEventDispatcher();
        fixture.Markets.Books = [fixture.Book("ETH", false)];
        fixture.Store.State = fixture.Store.State with
        {
            Mode = TradingMode.Paper,
            Paper = fixture.Store.State.Paper with
            {
                FundingThb = 5000,
                CashThb = 4000,
                Positions =
                [
                    new TradingHolding("ETH", 10, 1000, fixture.Time.Now, 100)
                    {
                        StopPrice = 95,
                        TargetPrice = 120,
                        TrailingDistance = 2,
                    },
                ],
            },
        };
        var source = new RealtimeWorkerEventSource();
        var engine = new TradingEngine(
            fixture.Store,
            fixture.Markets,
            fixture.Exchange,
            fixture.Account,
            fixture.Gate,
            fixture.Time,
            autonomous: fixture.Monitor,
            adaptiveRisk: fixture.Risk,
            events: events
        );
        var reports = new TradingReportDelivery(fixture.Store, new FakeTradingNotifier(), fixture.Time, fixture.Gate);
        using var worker = new TradingWorker(
            engine,
            reports,
            source,
            events,
            fixture.Store,
            fixture.Markets,
            fixture.Monitor,
            fixture.Time,
            NullLogger<TradingWorker>.Instance
        );
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await source.Required.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var book = fixture.Book("ETH", false) with { Bid = 94, Ask = 94.1m, Bars = [] };
            source.Publish(
                new(TradingEventKind.BookChanged, "ETH", 1, fixture.Time.Now, book: book, watchedSymbols: ["ETH"])
            );
            var deadline = Stopwatch.StartNew();
            while (fixture.Store.State.Paper.Fills.Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(10);
            }

            if (fixture.Store.State.Paper.Fills.Count != 1 || fixture.Exchange.Submissions != 0)
            {
                throw new InvalidOperationException(
                    "The running Paper worker did not exit on its first fresh stream quote. "
                        + System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                fixture.Store.State.Fault,
                                fixture.Store.State.Paper.Positions,
                                fixture.Store.State.Paper.Orders,
                                fixture.Store.State.Decisions,
                                Bid = events.LatestBook("ETH")?.Bid,
                                Status = events.Snapshot(),
                            }
                        )
                );
            }

            Console.WriteLine(
                FormattableString.Invariant(
                    $"Paper worker: SQL-watch recovery -> fake stream -> dispatcher -> exit fill in {deadline.Elapsed.TotalMilliseconds:0.0}ms; no broker call."
                )
            );
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
