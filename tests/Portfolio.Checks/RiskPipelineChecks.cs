using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class RiskPipelineChecks
{
    public static async Task Run()
    {
        await FreshnessAfterCollection();
        var time = new OrderCheckTime();
        var account = new AccountContext(new string('a', 64));
        var orders = new FakeOrderStore();
        var trading = new FakeTradingStore(
            TradingStateFactory.Create(time.Now) with
            {
                Mode = TradingMode.ManualReview,
            }
        );
        var store = new FakeRiskStore();
        var policy = new RiskPolicy { Profile = RiskProfile.Aggressive };
        await store.Configure(policy, store.Policy.PolicyHash, CancellationToken.None);
        var evidence = new FakeRiskEvidenceReader(
            new(time.Now, [new("THB", 1000, 1000)], [], [], time.Now.AddDays(-90))
        );
        var bars = Enumerable
            .Range(1, 21)
            .Select(i => new TradingBar(time.Now.ToUnixTimeSeconds() - (22 - i) * 300, 99, 100, 99, 99.9m, 100))
            .ToArray();
        var market = new TradingBook("BTC", time.Now, 99.9m, 100, 100, 100, bars);
        var markets = new FakeTradingMarketReader([market]);
        var capitalReader = new RiskCapitalReader(evidence, orders, trading, markets, account, time);
        var risk = new RiskSizingService(store, capitalReader, account, time, trading);
        var broker = new FakeOrderBroker { ExactBalances = evidence.Evidence.Balances };
        var create = new CreateOrderHandler(orders, broker, time, account, trading, risk);
        var source = Guid.NewGuid();
        var command = new CreateOrderCommand(
            "BTC",
            TradeSide.Buy,
            "1",
            "100",
            "Verified setup",
            source,
            account.AccountKey,
            new RiskSignal(99, null, time.Now, "fixture-completed-candle")
        );
        var view = await create.Handle(command, CancellationToken.None);
        var proposal =
            await orders.Find(source, CancellationToken.None)
            ?? throw new InvalidOperationException("Missing fixture proposal.");
        Check(
            proposal.Amount > 990 && proposal.Amount + proposal.EstimatedFeeThb <= 1000,
            "Portfolio replaces advisory amount and can use almost all cash with fee headroom."
        );
        Check(
            view.Sizing is not null
                && proposal.Sizing?.Signal.StopCandle == bars[^1].Timestamp
                && store.Decisions.Count == 1,
            "Verified candle and decision are retained before proposal persistence."
        );
        await create.Handle(command, CancellationToken.None);
        Check(store.Decisions.Count == 1, "Source retry returns the immutable proposal without rerunning sizing.");
        await Rejects(
            () => create.Handle(command with { Amount = "2" }, CancellationToken.None).AsTask(),
            "Source retry cannot replace immutable input."
        );
        var capital = (await capitalReader.Read("BTC", null, policy, CancellationToken.None)).Snapshot;
        Check(
            capital.SpendableThb == 1000 - proposal.Amount - proposal.EstimatedFeeThb
                && capital.ExposureThb == proposal.Amount,
            "Pending proposals reserve notional plus entry fees."
        );
        await Rejects(
            () => risk.Size(Guid.NewGuid(), "BTC", TradeSide.Buy, 10, 100, command.RiskSignal, CancellationToken.None),
            "Duplicate pending entry is blocked."
        );

        var approve = new ApproveOrderHandler(orders, broker, time, risk);
        trading.State = trading.State with { Mode = TradingMode.AutoLive };
        await Rejects(
            () => approve.Handle(new(source, true), CancellationToken.None).AsTask(),
            "Manual adaptive orders cannot submit while AutoLive is active."
        );
        trading.State = trading.State with { Mode = TradingMode.ManualReview };
        await Rejects(
            () =>
                risk.Revalidate(
                    proposal with
                    {
                        Sizing = proposal.Sizing is { } bound ? bound with { AccountKey = new string('b', 64) } : null,
                    },
                    CancellationToken.None
                ),
            "Account changes invalidate reviewed sizing."
        );
        using var gate = new SemaphoreSlim(1);
        var control = new TradingControl(
            trading,
            new FakeTradingExchange(),
            markets,
            account,
            new TradingControlOptions("fixture-owner") { LiveActivationAvailable = true },
            gate,
            time,
            orders
        );
        await Rejects(
            () =>
                control.Activate(
                    account.AccountKey,
                    trading.State.RulesHash,
                    time.Now.AddHours(1),
                    "enable-auto-trading",
                    CancellationToken.None
                ),
            "AutoLive cannot overlap an outstanding adaptive manual proposal.",
            "adaptive manual"
        );
        Check(
            trading.State.Mode == TradingMode.ManualReview && broker.Submissions == 0,
            "Failed activation preserves the owner's existing mode."
        );
        evidence.Evidence = evidence.Evidence with { Balances = [new("THB", 500, 500)] };
        broker.ExactBalances = evidence.Evidence.Balances;
        await Rejects(
            () => approve.Handle(new(source, true), CancellationToken.None).AsTask(),
            "Shrinking funds cannot silently resize the reviewed amount."
        );
        Check(
            broker.Submissions == 0 && (await orders.Find(source, CancellationToken.None))?.Amount == proposal.Amount,
            "Rejected revalidation sends nothing and preserves exact amount."
        );

        evidence.Evidence = evidence.Evidence with { Balances = [new("THB", 1000, 1000)] };
        broker.ExactBalances = [new("THB", 999, 999)];
        await Rejects(
            () => approve.Handle(new(source, true), CancellationToken.None).AsTask(),
            "Changes after risk reads cannot pass the final wallet check."
        );
        broker.ExactBalances = evidence.Evidence.Balances;
        await store.Configure(
            policy with
            {
                RiskToStopFraction = .04m,
            },
            store.Policy.PolicyHash,
            CancellationToken.None
        );
        await Rejects(
            () => approve.Handle(new(source, true), CancellationToken.None).AsTask(),
            "A changed policy needs a fresh reviewed proposal."
        );
        await store.Configure(policy, store.Policy.PolicyHash, CancellationToken.None);
        broker.FailSubmit = true;
        await approve.Handle(new(source, true), CancellationToken.None);
        await approve.Handle(new(source, true), CancellationToken.None);
        Check(
            broker.Submissions == 1 && (await orders.Find(source, CancellationToken.None))?.State == OrderState.Unknown,
            "Uncertain send has exactly one durable claim and is never retried."
        );
        Check(
            (await capitalReader.Read("BTC", null, policy, CancellationToken.None)).Snapshot.HasUncertainOrders,
            "Restart-visible uncertain proposals block capital reuse."
        );

        // Actual fill accounting must persist before a terminal status prevents further reconciliation.
        broker.Inspection = new(OrderState.Filled, "fixture", "verified")
        {
            Fills = [new("txn-1", 1, 100, .25m, 0, time.Now)],
        };
        var reconcile = new ReconcileOrderHandler(orders, broker, time, store);
        store.FailFills = true;
        await Rejects(
            () => reconcile.Handle(new(source), CancellationToken.None).AsTask(),
            "Unavailable fill audit cannot finalize accounting."
        );
        Check(
            (await orders.Find(source, CancellationToken.None))?.State == OrderState.Unknown,
            "Audit failure preserves recovery state."
        );
        store.FailFills = false;
        await reconcile.Handle(new(source), CancellationToken.None);
        Check(
            store.Fills.Count == 1 && broker.Submissions == 1,
            "Read-only reconciliation records verified fee evidence without sends."
        );

        evidence.Evidence = evidence.Evidence with
        {
            Balances = [new("THB", 1000, 600), new("ETH", 2, 1)],
            OpenOrders = [new("manual", "BTC", TradeSide.Buy, 400, 100)],
        };
        markets.Books = [market, market with { Symbol = "ETH", Bid = 100, Ask = 100.1m }];
        capital = (await capitalReader.Read("BTC", null, policy, CancellationToken.None)).Snapshot;
        Check(
            capital.EquityThb == 1200 && capital.SpendableThb == 600 && capital.ExposureThb == 600,
            "Manual reservations and marked held assets are counted once."
        );
        markets.Books = [market];
        Check(
            (await capitalReader.Read("BTC", null, policy, CancellationToken.None)).Snapshot.EquityThb is null,
            "Missing held-asset prices keep equity Unknown."
        );
        markets.Books = [market with { Bars = bars[..^1] }];
        await Rejects(
            () => risk.Size(Guid.NewGuid(), "BTC", TradeSide.Buy, 1, 100, command.RiskSignal, CancellationToken.None),
            "Incomplete or stale bars never produce an accepted size."
        );
        Performance(time.Now);
        var json = JsonSerializer.Serialize(proposal, PortfolioJson.CreateOptions());
        Check(
            JsonSerializer.Deserialize<OrderProposal>(json, PortfolioJson.CreateOptions())?.Sizing == proposal.Sizing
                || JsonSerializer.Deserialize<OrderProposal>(json, PortfolioJson.CreateOptions())?.Sizing?.Decision
                    == proposal.Sizing?.Decision,
            "Sizing persists with legacy proposal serialization."
        );
        var legacy = proposal with { Sizing = null };
        Check(
            JsonSerializer
                .Deserialize<OrderProposal>(
                    JsonSerializer.Serialize(legacy, PortfolioJson.CreateOptions()),
                    PortfolioJson.CreateOptions()
                )
                ?.Sizing
                is null,
            "Legacy orders retain nullable sizing."
        );
        Console.WriteLine(
            "Risk pipeline: adaptive capacity, reservations, immutable approval, uncertainty, fills, cash flows and compatibility passed; fake brokers only."
        );
    }

    private static async Task FreshnessAfterCollection()
    {
        var time = new OrderCheckTime();
        var started = time.Now;
        var policy = new RiskPolicy { Profile = RiskProfile.Aggressive };
        var evidence = new FakeRiskEvidenceReader(
            new(started, [new("THB", 1000, 1000), new("ETH", 1, 1)], [], [], started.AddDays(-90))
        );
        var market = new TradingBook("ETH", started.AddSeconds(6), 100, 101, 10, 10, []);
        var markets = new FakeTradingMarketReader([market]) { BeforeRead = () => time.Now = time.Now.AddSeconds(6) };
        var reader = new RiskCapitalReader(
            evidence,
            new FakeOrderStore(),
            new FakeTradingStore(TradingStateFactory.Create(started)),
            markets,
            new AccountContext(new string('a', 64)),
            time
        );
        var fresh = (await reader.Read(null, null, policy, CancellationToken.None)).Snapshot;
        Check(
            fresh.Blocker is null && fresh.EquityThb == 1100,
            "A fresh quote collected after a slow read is not future-dated relative to collection completion."
        );

        time.Now = started;
        evidence.Evidence = evidence.Evidence with { ObservedAt = started.AddSeconds(-85) };
        markets.Books = [market with { ObservedAt = started }];
        var staleAccount = (await reader.Read(null, null, policy, CancellationToken.None)).Snapshot;
        Check(
            staleAccount.Blocker == "Account evidence became stale during collection.",
            "Account evidence that expires while awaiting quotes must be blocked at completion."
        );

        time.Now = started;
        evidence.Evidence = evidence.Evidence with { ObservedAt = started };
        markets.Books = [market with { ObservedAt = started.AddSeconds(-85) }];
        var staleQuote = (await reader.Read(null, null, policy, CancellationToken.None)).Snapshot;
        Check(
            staleQuote.EquityThb is null && staleQuote.Blocker is not null,
            "A quote that expires during collection keeps total equity unknown."
        );

        time.Now = started;
        markets.Books = [market with { ObservedAt = started.AddSeconds(30) }];
        var futureQuote = (await reader.Read(null, null, policy, CancellationToken.None)).Snapshot;
        Check(
            futureQuote.EquityThb is null && futureQuote.Blocker is not null,
            "Genuinely future-dated quotes remain rejected after delayed collection."
        );
    }

    private static void Performance(DateTimeOffset now)
    {
        var previous = new CapitalSnapshot("account", now, 1000, 1000, 0, 0, [], false, null);
        var current = previous with { ObservedAt = now.AddHours(1), EquityThb = 2010, SpendableThb = 2010 };
        var flow = new CapitalCashFlow("deposit", "THB", 1000, 0, now.AddMinutes(30), CashFlowKind.Deposit, 1000);
        var result = RiskPerformanceCalculator.Calculate(previous, current, [flow], now.AddDays(-1));
        Check(
            result.NetEquityChangeThb == 10 && result.TimeWeightedReturn is null,
            "Deposits are excluded from performance; exact TWR stays Unknown without flow-time valuation."
        );
        result = RiskPerformanceCalculator.Calculate(previous, current with { EquityThb = 900 }, [], now.AddDays(-1));
        Check(
            result.NetEquityChangeThb == -100 && result.TimeWeightedReturn == -.1m,
            "Losses remain signed and flow-free return is measured."
        );
        Check(
            RiskPerformanceCalculator
                .Calculate(previous, current, [flow with { ValueThb = null }], now.AddDays(-1))
                .NetEquityChangeThb
                is null,
            "Unvalued crypto transfers never become profit."
        );
        Check(
            RiskPerformanceCalculator.Calculate(previous, current, [], now.AddMinutes(1)).TimeWeightedReturn is null,
            "History coverage gaps remain Unknown."
        );
    }

    private static async Task Rejects(Func<Task> operation, string message, string? expectedError = null)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
            when (exception is OrderValidationException or InvalidOperationException or ArgumentException)
        {
            if (expectedError is not null && !exception.Message.Contains(expectedError, StringComparison.Ordinal))
            {
                throw;
            }

            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
