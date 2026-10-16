using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class AutonomousTradingChecks
{
    public static async Task Run()
    {
        var time = new OrderCheckTime();
        var rules = new TradingRuleSet
        {
            Version = "adaptive-intraday-v1",
            Symbols = [],
            Adaptive = new AdaptiveTradingPolicy(),
        };
        rules.Validate();
        var legacy = new TradingRuleSet();
        const string original =
            """{"Version":"ema20-50-breakout-v1","Symbols":["BTC"],"AllocatedThb":1000,"PerOrderThb":100,"DailyLossThb":100,"MaxSpreadBps":50,"SlippageBps":20,"TakeProfitBps":200,"StopLossBps":100,"TrailingStopBps":100,"FeeBps":25,"VolumeRatio":1.5,"MaxPositions":2,"MaxDataAgeSeconds":90,"TimeExitMinutes":120,"CooldownMinutes":15,"OrderLifetimeMinutes":15}""";
        var hash = Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original)))
            .ToLowerInvariant();
        Check(TradingRuleHash.Compute(legacy) == hash, "Legacy rules hashes must remain byte-for-byte compatible.");
        var store = new FakeTradingStore(
            TradingStateFactory.Create(time.Now) with
            {
                Rules = rules,
                RulesHash = TradingRuleHash.Compute(rules),
                Mode = TradingMode.Stopped,
            }
        );
        var markets = new FakeTradingMarketReader([]);
        var evidence = new FakeRiskEvidenceReader(
            new(time.Now, [new Balance("THB", 5000, 5000)], [], [], time.Now.AddDays(-90))
        );
        var riskStore = new FakeRiskStore();
        var orders = new FakeOrderStore();
        var capital = new RiskCapitalReader(evidence, orders, store, markets, new AccountContext("test-account"), time);
        var risk = new AutonomousRiskService(capital, riskStore, new AccountContext("test-account"), time);
        var initial = await risk.Read(rules, true, CancellationToken.None);
        Check(
            initial.Snapshot.SpendableThb == 5000 && initial.Snapshot.Blocker is null,
            "Cash-only account must not require historic profit or assets."
        );
        evidence.Evidence = evidence.Evidence with { Balances = [new Balance("THB", 10000, 10000)] };
        var changed = await risk.Read(rules, true, CancellationToken.None);
        Check(
            changed.Snapshot.EquityThb == 10000,
            "New deposits must change actual capital without changing policy hash."
        );
        Check(store.State.Mode == TradingMode.Stopped, "Reading or sizing never enables live authority.");
        await LiveAdaptiveEntry();
        Console.WriteLine("Autonomous trading checks passed.");
    }

    private static async Task LiveAdaptiveEntry()
    {
        var time = new OrderCheckTime();
        var account = new AccountContext("test-account");
        var policy = new AdaptiveTradingPolicy();
        var rules = new TradingRuleSet
        {
            Version = "adaptive-intraday-v1",
            Symbols = [],
            Adaptive = policy,
        };
        var initial = TradingStateFactory.Create(time.Now) with
        {
            Rules = rules,
            RulesHash = TradingRuleHash.Compute(rules),
            Mode = TradingMode.Stopped,
        };
        var store = new FakeTradingStore(initial);
        var markets = new FakeTradingMarketReader([Book("ETH", time), MalformedBook("BTC", time)])
        {
            Universe = new(time.Now, ["BTC", "ETH"], 2, 0, null),
        };
        var exchange = new FakeTradingExchange { AvailableCash = 5000 };
        var evidence = new FakeRiskEvidenceReader(
            new(time.Now, [new Balance("THB", 5000, 5000)], [], [], time.Now.AddDays(-90))
        );
        var orders = new FakeOrderStore();
        var riskStore = new FakeRiskStore();
        using var gate = new SemaphoreSlim(1);
        var capital = new RiskCapitalReader(evidence, orders, store, markets, account, time);
        var risk = new AutonomousRiskService(capital, riskStore, account, time);
        var monitor = new AutonomousMarketMonitor(markets, risk, time);
        var control = new TradingControl(
            store,
            exchange,
            markets,
            account,
            new TradingControlOptions("owner-token"),
            gate,
            time,
            adaptiveRisk: risk
        );
        await control.Activate(
            account.AccountKey,
            store.State.RulesHash,
            time.Now.AddHours(1),
            "enable-auto-trading",
            CancellationToken.None
        );
        var engine = new TradingEngine(
            store,
            markets,
            exchange,
            account,
            gate,
            time,
            autonomous: monitor,
            adaptiveRisk: risk
        );
        await engine.Tick(CancellationToken.None);
        Check(store.State.Mode == TradingMode.AutoLive, "adaptive mandate activates with a reviewed account hash");
        Check(
            store.State.Live.Orders.Any(order => order.Symbol == "ETH"),
            "adaptive engine selects ETH from the universe"
        );
        Check(
            store.State.Live.Orders.All(order => order.Amount != 100m),
            "adaptive entry does not use legacy fixed order amount"
        );
        Check(exchange.Submissions == 1, "adaptive entry submits through the fake broker once");
        Check(
            store.State.Autonomy?.Markets.Any(item => item.Symbol == "BTC") == true,
            "bad candidate does not block valid candidate"
        );
    }

    private static TradingBook Book(string symbol, OrderCheckTime time)
    {
        var end = time.Now.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index => new TradingBar(
                end - (51 - index) * 300,
                100m,
                index == 50 ? 111m : 101m,
                99m,
                index == 50 ? 110m : 100m,
                index == 50 ? 2m : 1m
            ))
            .ToArray();
        return new TradingBook(symbol, time.Now, 109.9m, 110m, 10m, 10m, bars);
    }

    private static TradingBook MalformedBook(string symbol, OrderCheckTime time) =>
        Book(symbol, time) with
        {
            Bars = Book(symbol, time).Bars.Skip(1).ToArray(),
        };

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
