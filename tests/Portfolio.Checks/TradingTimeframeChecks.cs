using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingTimeframeChecks
{
    public static async Task Run()
    {
        using (var upgrade = new AutonomousFixture())
        {
            await upgrade.Activate();
            var saved = upgrade.Store.State;
            var changed = await upgrade.Control.Configure(
                upgrade.Rules with
                {
                    Version = "adaptive-intraday-v2",
                },
                CancellationToken.None
            );
            Check(
                changed.Mode == TradingMode.Stopped
                    && changed.Mandate?.Id == saved.Mandate?.Id
                    && changed.Mandate?.RevokedAt is not null
                    && changed.Live == saved.Live
                    && changed.RulesHash != saved.RulesHash
                    && upgrade.Exchange.Submissions == 0,
                "An explicit v2 configuration revokes existing authority while preserving balances and history."
            );
        }

        using var fixture = new AutonomousFixture();
        var rules = fixture.Rules with { Version = "adaptive-intraday-v2" };
        rules.Validate();
        Check(
            TradingRuleHash.Compute(rules) != TradingRuleHash.Compute(fixture.Rules),
            "Multi-timeframe authority must have a new hash."
        );
        foreach (var interval in new[] { 300, 900, 3600 })
        {
            var end = fixture.Time.Now.ToUnixTimeSeconds() / interval * interval;
            var original = fixture.Book("ETH");
            var book = original with
            {
                TimeframeSeconds = interval,
                Bars = original
                    .Bars.Select((bar, index) => bar with { Timestamp = end - (51 - index) * interval })
                    .ToArray(),
            };
            var result = AdaptiveTradingStrategy.Evaluate(rules, book, null, fixture.Time.Now);
            Check(
                result.Action == TradingAction.Buy && result.TimeframeSeconds == interval,
                "A native completed timeframe must produce a repeatable signal."
            );
            Check(
                result.StrategyVersion == $"breakout-v2-{interval / 60}m",
                "Signal provenance must identify its timeframe."
            );
            if (interval != 300)
            {
                Check(
                    AdaptiveTradingStrategy.Evaluate(fixture.Rules, book, null, fixture.Time.Now).Action
                        == TradingAction.Wait,
                    "The original mandate never widens to another timeframe."
                );
            }

            var stale = book with
            {
                Bars = book.Bars.Select(bar => bar with { Timestamp = bar.Timestamp - interval }).ToArray(),
            };
            Check(
                AdaptiveTradingStrategy.Evaluate(rules, stale, null, fixture.Time.Now).Action == TradingAction.Wait,
                "An older complete window does not authorize a current entry."
            );
        }

        var nativeEnd = TradingTimeframes.LastCompletedOpen(fixture.Time.Now, 900) + 900;
        var market = fixture.Book("ETH") with
        {
            TimeframeSeconds = 900,
            Bars = fixture
                .Book("ETH")
                .Bars.Select((bar, index) => bar with { Timestamp = nativeEnd - (51 - index) * 900 })
                .ToArray(),
        };
        fixture.Markets.Books = [market];
        fixture.Store.State = fixture.Store.State with { Rules = rules, RulesHash = TradingRuleHash.Compute(rules) };
        await fixture.Activate();
        await fixture.Monitor.Read(fixture.Store.State, CancellationToken.None);
        var trigger = new TradingMarketEvent(
            TradingEventKind.CandleClosed,
            "ETH",
            0,
            fixture.Time.Now,
            candleAt: fixture.Time.Now.ToUnixTimeSeconds() / 300 * 300 - 300
        );
        await fixture.Engine.Process(trigger, CancellationToken.None);
        Check(
            fixture.Exchange.Submissions == 1,
            "The native 15-minute signal must survive the five-minute event and risk preflight. "
                + System.Text.Json.JsonSerializer.Serialize(fixture.Store.State.Decisions)
                + " Fault: "
                + fixture.Store.State.Fault
        );
        await fixture.Engine.Process(trigger, CancellationToken.None);
        Check(fixture.Exchange.Submissions == 1, "Repeated candle events never submit a duplicate order.");
        Check(
            fixture.Store.State.Decisions.Any(item =>
                item.TimeframeSeconds == 900 && item.SignalAction == TradingAction.Buy
            ),
            "The durable decision retains timeframe and original signal for the daily funnel."
        );
        var holding = new TradingHolding("ETH", 1, 100, fixture.Time.Now, 110) { StopPrice = 99 };
        var exit = AdaptiveTradingStrategy.Evaluate(
            rules,
            market with
            {
                Bid = 98,
                Ask = 98.1m,
                Bars = [],
            },
            holding,
            fixture.Time.Now
        );
        Check(exit.Action == TradingAction.Sell, "Owned stop exits never wait for historical candles.");
        Console.WriteLine(
            "Native timeframe, unchanged mandates, risk preflight, event deduplication and exit checks passed."
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
