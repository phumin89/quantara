using System.Collections;
using System.Reflection;
using Quantara.MarketData.Infrastructure;

namespace Quantara.MarketData.Checks;

public static class MarketSessionChecks
{
    public static async Task Run()
    {
        var reconcile =
            typeof(LiveMarketFeed).GetMethod("ReconcileSessions", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Session reconciliation is unavailable.");
        var sessions =
            Activator.CreateInstance(reconcile.GetParameters()[1].ParameterType) as IDictionary
            ?? throw new InvalidOperationException("Session storage is unavailable.");
        var events = new List<string>();
        async Task Start(MarketWatchSymbol symbol, CancellationToken token)
        {
            events.Add($"start:{symbol.Symbol}:{symbol.PairingId}");
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                events.Add($"stop:{symbol.Symbol}:{symbol.PairingId}");
            }
        }

        async Task Update(params MarketWatchSymbol[] desired)
        {
            var pending =
                reconcile.Invoke(
                    null,
                    [desired, sessions, (Func<MarketWatchSymbol, CancellationToken, Task>)Start, CancellationToken.None]
                ) as Task
                ?? throw new InvalidOperationException("Reconciliation did not return a task.");
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var bitcoin = new MarketWatchSymbol("BTC_THB", 42, 1000);
        var ether = new MarketWatchSymbol("ETH_THB", 7, 100);
        try
        {
            await Update(bitcoin, ether);
            await Update(bitcoin with { QuoteVolume = 9000 }, ether);
            Check(
                events.SequenceEqual(["start:BTC_THB:42", "start:ETH_THB:7"]),
                "volume-only changes keep both sessions"
            );
            await Update(bitcoin with { PairingId = 43 }, ether);
            Check(
                events.SequenceEqual(["start:BTC_THB:42", "start:ETH_THB:7", "stop:BTC_THB:42", "start:BTC_THB:43"]),
                "new pairing cancels and awaits the old session before replacement"
            );
            Check(sessions.Count == 2, "replacement keeps the bounded session count");
            await Update(ether);
            Check(events[^1] == "stop:BTC_THB:43" && sessions.Count == 1, "removal stops only the removed market");
            await Update();
            Check(events[^1] == "stop:ETH_THB:7" && sessions.Count == 0, "empty watch cleans up all sessions");
        }
        finally
        {
            await Update();
        }

        Console.WriteLine("Market session identity and cancellation checks passed without sockets.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Market session check failed: " + message);
        }
    }
}
