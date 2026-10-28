using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;

namespace Portfolio.Checks;

public static class PortfolioGoalChecks
{
    public static Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        var snapshot = new CapitalSnapshot(
            account,
            now,
            3000,
            7500,
            4500,
            2000,
            [new("THB", 5000, 3000, 1, 0), new("BTC", 1, 1, 2500, 0)],
            false,
            null
        );
        var funding = new CapitalCashFlow("funding", "THB", 5000, 0, now.AddDays(-1), CashFlowKind.Deposit, 5000);
        var source = new RiskAccountEvidence(now, [], [], [funding, funding], PortfolioObjective.StartsAt);
        var captured = PortfolioGoalAccounting.Capture(account, new(snapshot, source, []), now);
        Check(
            captured.PortfolioValueThb == 7500 && captured.DepositsThb == 5000 && captured.TradingPnlThb is null,
            "Total wallet value includes reserved cash once; confirmed deposits are distinct from unknown full-account profit."
        );
        using var view = JsonDocument.Parse(
            JsonSerializer.Serialize(PortfolioGoalAccounting.View(account, captured, now))
        );
        Check(
            view.RootElement.GetProperty("remainingThb").GetString() == "2500",
            "Remaining target is based on total equity."
        );
        Check(
            PortfolioGoalAccounting
                .Capture(account, new(snapshot with { AccountKey = "other" }, source, []), now)
                .PortfolioValueThb
                is null,
            "Other account cannot contribute to the goal."
        );
        Check(
            PortfolioGoalAccounting
                .Capture(account, new(snapshot with { HasUncertainOrders = true }, source, []), now)
                .PortfolioValueThb
                is null,
            "Uncertain partial order outcomes cannot produce a falsely coherent valuation."
        );
        Check(
            PortfolioGoalAccounting
                .Capture(account, new(snapshot with { Blocker = "Missing quote" }, source, []), now)
                .PortfolioValueThb
                is null,
            "Unknown valuation is preserved."
        );
        Check(
            PortfolioGoalAccounting
                .Capture(account, new(snapshot, source with { CashFlowCoverageFrom = now.AddDays(-1) }, []), now)
                .DepositsThb
                is null,
            "Incomplete funding coverage is not zero deposits."
        );
        var quote = new Quantara.Portfolio.Domain.Trading.TradingBook("BTC", now, 2500, 2501, 1, 1, []);
        foreach (var age in new[] { 90, 91, 300 })
        {
            var priced = PortfolioGoalAccounting.Capture(
                account,
                new(snapshot, source, [quote with { ObservedAt = now.AddSeconds(-age) }]),
                now
            );
            Check(
                age == PortfolioObjective.MaximumAgeSeconds
                    ? priced.PortfolioValueThb == 7500 && priced.Reason is null
                    : priced.PortfolioValueThb is null && priced.Reason is not null,
                "A fresh wallet cannot make an older held-asset quote satisfy the objective freshness limit."
            );
        }

        var cashOnly = PortfolioGoalAccounting.Capture(
            account,
            new(
                snapshot with
                {
                    EquityThb = 5000,
                    Assets = [new("THB", 5000, 3000, 1, 0), new("BTC", 0, 0, 2500, 0)],
                },
                source,
                [quote with { ObservedAt = now.AddSeconds(-300) }]
            ),
            now
        );
        Check(
            cashOnly.PortfolioValueThb == 5000 && cashOnly.Reason is null,
            "An unused quote does not prevent a fresh cash-only objective valuation."
        );
        var cutoff = PortfolioObjective.DeadlineExclusive;
        var before = captured with { ObservedAt = cutoff.AddSeconds(-20), PortfolioValueThb = 10500 };
        using var final = JsonDocument.Parse(
            JsonSerializer.Serialize(PortfolioGoalAccounting.View(account, before, cutoff.AddDays(1)))
        );
        Check(
            final.RootElement.GetProperty("portfolioValueThb").GetString() == "10500"
                && final.RootElement.GetProperty("measurement").GetString() == "monthEnd",
            "Bangkok month-end uses the last fresh pre-cutoff checkpoint."
        );
        foreach (
            var invalid in new[]
            {
                before with
                {
                    ObservedAt = cutoff,
                },
                before with
                {
                    ObservedAt = cutoff.AddSeconds(-100),
                },
                before with
                {
                    AccountKey = "other",
                },
            }
        )
        {
            using var rejected = JsonDocument.Parse(
                JsonSerializer.Serialize(PortfolioGoalAccounting.View(account, invalid, cutoff.AddDays(1)))
            );
            Check(
                rejected.RootElement.GetProperty("portfolioValueThb").ValueKind == JsonValueKind.Null,
                "Post-cutoff, stale and other-account observations never certify the month-end goal."
            );
        }

        var lateBook = new Quantara.Portfolio.Domain.Trading.TradingBook(
            "BTC",
            cutoff.AddSeconds(1),
            2500,
            2501,
            1,
            1,
            []
        );
        var mixed = PortfolioGoalAccounting.Capture(
            account,
            new(
                snapshot with
                {
                    ObservedAt = cutoff.AddSeconds(-1),
                },
                source with
                {
                    ObservedAt = cutoff.AddSeconds(-1),
                },
                [lateBook]
            ),
            cutoff.AddSeconds(2)
        );
        Check(mixed.ObservedAt > cutoff, "A post-cutoff quote cannot be relabelled as a pre-cutoff valuation.");
        Console.WriteLine(
            "Portfolio objective: reserved cash once, account scope, deposit coverage, Unknown P&L, frozen Bangkok cutoff and coherent observation watermark passed."
        );
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
