using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingControlChecks
{
    public static async Task Run()
    {
        var time = new OrderCheckTime();
        var account = new string('a', 64);
        var store = new FakeTradingStore(TradingStateFactory.Create(time.Now));
        var market = new FakeTradingMarketReader([new TradingBook("BTC", time.Now, 100, 100.1m, 10, 10, [])]);
        var exchange = new FakeTradingExchange();
        using var gate = new SemaphoreSlim(1);
        var control = new TradingControl(
            store,
            exchange,
            market,
            new AccountContext(account),
            new TradingControlOptions("fake-owner-token"),
            gate,
            time
        );
        await Refused(() => control.SetMode(TradingMode.AutoLive, CancellationToken.None));
        await Refused(() =>
            control.Activate(
                new string('b', 64),
                store.State.RulesHash,
                time.Now.AddHours(1),
                "enable-auto-trading",
                CancellationToken.None
            )
        );
        await Refused(() =>
            control.Activate(account, "wrong-hash", time.Now.AddHours(1), "enable-auto-trading", CancellationToken.None)
        );
        await Refused(() =>
            control.Activate(account, store.State.RulesHash, time.Now.AddHours(1), "", CancellationToken.None)
        );
        await Refused(() =>
            control.Activate(
                account,
                store.State.RulesHash,
                time.Now.AddDays(31),
                "enable-auto-trading",
                CancellationToken.None
            )
        );
        Check(
            store.State.Mandate is null && store.State.Mode == TradingMode.Paper && exchange.Submissions == 0,
            "Rejected activation grants no authority or broker side effect."
        );
        var activated = await control.Activate(
            account,
            store.State.RulesHash,
            time.Now.AddHours(1),
            "enable-auto-trading",
            CancellationToken.None
        );
        var duplicate = await control.Activate(
            account,
            store.State.RulesHash,
            time.Now.AddHours(1),
            "enable-auto-trading",
            CancellationToken.None
        );
        Check(
            activated.Mandate == duplicate.Mandate && activated.Revision == duplicate.Revision,
            "Repeated owner activation is idempotent for the exact mandate."
        );
        await Refused(() =>
            control.Activate(
                account,
                store.State.RulesHash,
                time.Now.AddHours(2),
                "enable-auto-trading",
                CancellationToken.None
            )
        );
        var configured = await control.Configure(store.State.Rules with { PerOrderThb = 50 }, CancellationToken.None);
        Check(
            configured.Mode == TradingMode.Stopped
                && configured.Mandate?.RevokedAt == time.Now
                && configured.Live.FundingThb == 1000,
            "Changing material rules revokes live authority while preserving capital history."
        );
        await control.SetMode(TradingMode.Paper, CancellationToken.None);
        var funded = await control.Configure(store.State.Rules with { AllocatedThb = 1500 }, CancellationToken.None);
        Check(
            funded.Paper.CashThb == 1500 && funded.Live.CashThb == 1500 && funded.Paper.Equity[0].FundingThb == 1000,
            "Funding adjustments preserve earlier marks and never manufacture trading profit."
        );
        store.State = store.State with
        {
            Live = store.State.Live with { Positions = [new TradingHolding("BTC", 1, 100, time.Now, 100)] },
        };
        await Refused(() => control.Configure(store.State.Rules with { Symbols = ["ETH"] }, CancellationToken.None));
        await Refused(() => control.Configure(store.State.Rules with { AllocatedThb = 90 }, CancellationToken.None));
        Check(
            exchange.Submissions == 0 && exchange.Cancellations == 0,
            "Owner control tests use no broker transactions."
        );
        Console.WriteLine(
            "Trading control checks passed: exact mandate, expiry, idempotency, rule changes and retained capital."
        );
    }

    private static async Task Refused(Func<Task<TradingState>> operation)
    {
        try
        {
            await operation();
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid trading control must be rejected.");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
