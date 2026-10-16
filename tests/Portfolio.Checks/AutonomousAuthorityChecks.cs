using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class AutonomousAuthorityChecks
{
    public static async Task Run()
    {
        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Exchange.FailSubmission = true;
            fixture.Exchange.BeforeSend = (order, cancellation) =>
                Check(
                    !cancellation
                        && fixture.Store.State.Live.Orders.Single(item => item.Id == order.Id).Status == "submitting",
                    "Submission must follow its exact durable claim."
                );
            await fixture.Engine.Tick(CancellationToken.None);
            await fixture.Restart().Tick(CancellationToken.None);
            Check(
                fixture.Exchange.Submissions == 1 && fixture.Store.State.Live.Orders.Single().Status == "unknown",
                "Unknown submissions must never retry after restart."
            );
        }

        foreach (var invalid in new[] { "account", "expiry", "disable", "hash" })
        {
            using var fixture = new AutonomousFixture();
            await fixture.Activate();
            var state = fixture.Store.State;
            var mandate = state.Mandate ?? throw new InvalidOperationException("Fixture activation failed.");
            fixture.Store.State = invalid switch
            {
                "account" => state with { Mandate = mandate with { AccountKey = "different-account" } },
                "expiry" => state with { Mandate = mandate with { ExpiresAt = fixture.Time.Now } },
                "hash" => state with { Mandate = mandate with { RulesHash = "different-hash" } },
                _ => await fixture.Control.Disable(false, CancellationToken.None),
            };
            await fixture.Engine.Tick(CancellationToken.None);
            Check(fixture.Exchange.Submissions == 0, "Invalid or revoked authority must never submit: " + invalid);
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Markets.Books = [fixture.Book("ETH"), fixture.Book("BTC")];
            fixture.Markets.Universe = new(fixture.Time.Now, ["ETH", "BTC"], 2, 0, null);
            await fixture.Engine.Tick(CancellationToken.None);
            var committed = fixture
                .Store.State.Live.Orders.Where(TradingLedgerAccounting.Active)
                .Sum(order => order.Amount * (1 + fixture.Rules.FeeBps / 10000));
            Check(committed <= 5000, "Multiple candidates must not spend the same cached cash twice.");
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Time.Now = fixture.Time.Now.AddSeconds(60);
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with
            {
                ObservedAt = fixture.Time.Now,
                Balances = [new Balance("THB", 10000, 10000)],
                CashFlows =
                [
                    new CapitalCashFlow("new-deposit", "THB", 5000, 0, fixture.Time.Now, CashFlowKind.Deposit, 5000),
                ],
            };
            fixture.Markets.Books = [fixture.Book("ETH", false)];
            fixture.Markets.Universe = new(fixture.Time.Now, ["ETH"], 1, 0, null);
            await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
            await fixture.Engine.Tick(CancellationToken.None);
            await fixture.Engine.Tick(CancellationToken.None);
            Check(
                fixture.Store.State.Live.CashThb == 10000
                    && fixture.Store.State.Live.FundingThb == 10000
                    && fixture.Store.State.Live.Fills.Count == 0,
                "Verified deposits update capital once, without creating profit or fills."
            );
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Store.State = fixture.Store.State with
            {
                Live = fixture.Store.State.Live with
                {
                    CashThb = 3900,
                    Positions = [new TradingHolding("ETH", 10, 1100, fixture.Time.Now, 110)],
                },
            };
            fixture.Evidence.Evidence = fixture.Evidence.Evidence with
            {
                Balances = [new Balance("THB", 3900, 3900), new Balance("ETH", 10, 10)],
            };
            fixture.Markets.Books = [fixture.Book("ETH") with { Bid = 113, Ask = 114m }];
            await fixture.Risk.Read(fixture.Rules, true, CancellationToken.None);
            await fixture.Engine.Tick(CancellationToken.None);
            var exit = fixture.Store.State.Live.Orders.Single();
            Check(
                exit.Side == TradeSide.Sell && exit.Amount == 10 && fixture.Exchange.Submissions == 1,
                "Adaptive exits sell the owned quantity instead of the legacy entry amount cap."
            );
        }

        using (var fixture = new AutonomousFixture())
        {
            await fixture.Activate();
            fixture.Store.State = fixture.Store.State with { Live = fixture.Store.State.Live with { CashThb = 1 } };
            await fixture.Engine.Tick(CancellationToken.None);
            Check(
                fixture.Exchange.Submissions == 0 && fixture.Store.State.Fault is not null,
                "Unreconciled strategy funding must not create a broker fill that cannot be accounted for."
            );
        }

        using (var fixture = new AutonomousFixture())
        {
            fixture.Store.State = fixture.Store.State with { Mode = TradingMode.Paper };
            await fixture.Engine.Tick(CancellationToken.None);
            Check(
                fixture.Exchange.Submissions == 0
                    && fixture.Store.State.Paper.Fills.Count > 0
                    && fixture.Store.State.Paper.CashThb >= 0,
                "Adaptive Paper must use simulated capital without real broker submissions."
            );
            Check(fixture.Audit.Decisions.Count == 0, "Simulated sizing must not enter real-account risk audits.");
        }

        Console.WriteLine("Autonomous authority, capital and lifecycle checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
