using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingActivationPrecisionChecks
{
    public static async Task Run()
    {
        const decimal rawQuantity = 1877.22m / 43.34m;
        const decimal creditedQuantity = 43.31379787m;

        await Adaptive(rawQuantity, creditedQuantity, creditedQuantity, true);
        await Adaptive(rawQuantity, creditedQuantity, creditedQuantity - 0.00000001m, false);
        await Adaptive(rawQuantity, creditedQuantity, null, false);
        await Adaptive(rawQuantity, creditedQuantity, null, false, missing: true);
        await Adaptive(
            rawQuantity,
            creditedQuantity,
            creditedQuantity,
            false,
            verificationDelay: TimeSpan.FromMinutes(2)
        );
        await Adaptive(
            rawQuantity,
            creditedQuantity,
            creditedQuantity,
            false,
            verificationDelay: TimeSpan.FromHours(2)
        );
        await Fixed(rawQuantity, creditedQuantity, true);
        await Fixed(rawQuantity, creditedQuantity - 0.00000001m, false);

        Console.WriteLine("Trading activation precision: legacy holdings require exact verified broker quantity.");
    }

    private static async Task Adaptive(
        decimal rawQuantity,
        decimal creditedQuantity,
        decimal? available,
        bool accepted,
        bool missing = false,
        TimeSpan? verificationDelay = null
    )
    {
        using var fixture = new AutonomousFixture();
        var position = new TradingHolding("ETH", rawQuantity, 1877.22m, fixture.Time.Now.AddMinutes(-5), 110m);
        fixture.Store.State = fixture.Store.State with
        {
            Live = fixture.Store.State.Live with { CashThb = 3152.21m, FundingThb = 5029.43m, Positions = [position] },
        };
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with
        {
            Balances = missing
                ? [new Balance("THB", 3152.21m, 3152.21m)]
                : [new Balance("THB", 3152.21m, 3152.21m), new Balance("ETH", creditedQuantity, available)],
        };
        fixture.Exchange.BeforeMarket = () => fixture.Time.Now += verificationDelay ?? TimeSpan.Zero;

        var fills = fixture.Store.State.Live.Fills;
        var result = await TryActivate(fixture);
        if (result != accepted || fixture.Exchange.Submissions != 0 || fixture.Exchange.Cancellations != 0)
        {
            throw new InvalidOperationException(
                "Adaptive activation precision acceptance or broker side effects were incorrect."
            );
        }

        var live = fixture.Store.State.Live;
        if (
            live.Positions.Single() != position
            || live.Fills.Count != fills.Count
            || live.Fills.Zip(fills).Any(pair => pair.First != pair.Second)
            || (
                !accepted
                && (fixture.Store.State.Mode != TradingMode.Stopped || fixture.Store.State.Mandate is not null)
            )
        )
        {
            throw new InvalidOperationException(
                "Activation precision changed legacy ownership evidence or retained authority after refusal."
            );
        }
    }

    private static async Task Fixed(decimal rawQuantity, decimal creditedQuantity, bool accepted)
    {
        var time = new OrderCheckTime();
        var rules = new TradingRuleSet { Symbols = ["ETH"], AllocatedThb = 5029.43m };
        var state = TradingStateFactory.Create(time.Now) with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Mode = TradingMode.Stopped,
            Live = TradingStateFactory.Create(time.Now).Live with
            {
                CashThb = 3152.21m,
                FundingThb = 5029.43m,
                Positions = [new TradingHolding("ETH", rawQuantity, 1877.22m, time.Now, 110m)],
            },
        };
        var store = new FakeTradingStore(state);
        var market = new FakeTradingMarketReader([new TradingBook("ETH", time.Now, 100, 100.1m, 1, 1, [])]);
        var original = state.Live.Positions.Single();
        var originalFills = state.Live.Fills;
        var exchange = new FakeTradingExchange { AvailableCash = 3152.21m, AvailableBase = creditedQuantity };
        using var gate = new SemaphoreSlim(1);
        var control = new TradingControl(
            store,
            exchange,
            market,
            new AccountContext("test-account"),
            new TradingControlOptions("owner-token"),
            gate,
            time
        );
        var result = await TryActivate(control, time, state);
        if (result != accepted || exchange.Submissions != 0 || exchange.Cancellations != 0)
        {
            throw new InvalidOperationException(
                "Fixed-policy activation precision acceptance or broker side effects were incorrect."
            );
        }

        if (
            store.State.Live.Positions.Single() != original
            || store.State.Live.Fills.Count != originalFills.Count
            || store.State.Live.Fills.Zip(originalFills).Any(pair => pair.First != pair.Second)
            || (!accepted && (store.State.Mode != TradingMode.Stopped || store.State.Mandate is not null))
        )
        {
            throw new InvalidOperationException(
                "Fixed-policy activation changed legacy ownership evidence or retained authority after refusal."
            );
        }
    }

    private static async Task<bool> TryActivate(AutonomousFixture fixture)
    {
        try
        {
            await fixture.Activate();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    private static async Task<bool> TryActivate(TradingControl control, OrderCheckTime time, TradingState state)
    {
        try
        {
            await control.Activate(
                "test-account",
                state.RulesHash,
                time.Now.AddHours(1),
                "enable-auto-trading",
                CancellationToken.None
            );
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
