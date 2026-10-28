using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class AutonomousFixture : IDisposable
{
    public OrderCheckTime Time { get; } = new();

    public AccountContext Account { get; }

    public TradingRuleSet Rules { get; } =
        new()
        {
            Version = "adaptive-intraday-v1",
            Symbols = [],
            Adaptive = new(),
            MaxPositions = 16,
        };

    public FakeTradingStore Store { get; }

    public FakeTradingMarketReader Markets { get; }

    public FakeRiskEvidenceReader Evidence { get; }

    public FakeTradingExchange Exchange { get; } = new() { AvailableCash = 5000 };

    public FakeRiskStore Audit { get; } = new();

    public AutonomousRiskService Risk { get; }

    public AutonomousMarketMonitor Monitor { get; }

    public TradingControl Control { get; }

    public TradingEngine Engine { get; }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public AutonomousFixture(string accountKey = "test-account")
    {
        this.Account = new(accountKey);
        this.Store = new(
            TradingStateFactory.Create(this.Time.Now) with
            {
                Rules = this.Rules,
                RulesHash = TradingRuleHash.Compute(this.Rules),
                Mode = TradingMode.Stopped,
            }
        );
        this.Markets = new([this.Book("ETH")]) { Universe = new(this.Time.Now, ["ETH"], 1, 0, null) };
        this.Evidence = new(new(this.Time.Now, [new Balance("THB", 5000, 5000)], [], [], this.Time.Now.AddDays(-90)));
        var capital = new RiskCapitalReader(
            this.Evidence,
            new FakeOrderStore(),
            this.Store,
            this.Markets,
            this.Account,
            this.Time
        );
        this.Risk = new(capital, this.Audit, this.Account, this.Time);
        this.Monitor = new(this.Markets, this.Risk, this.Time);
        this.Control = new(
            this.Store,
            this.Exchange,
            this.Markets,
            this.Account,
            new("owner-token"),
            this.Gate,
            this.Time,
            adaptiveRisk: this.Risk
        );
        this.Engine = this.Restart();
    }

    public Task<TradingState> Activate() =>
        this.Control.Activate(
            this.Account.AccountKey,
            this.Store.State.RulesHash,
            this.Time.Now.AddHours(1),
            "enable-auto-trading",
            CancellationToken.None
        );

    public TradingEngine Restart() =>
        new(
            this.Store,
            this.Markets,
            this.Exchange,
            this.Account,
            this.Gate,
            this.Time,
            autonomous: this.Monitor,
            adaptiveRisk: this.Risk
        );

    public TradingBook Book(string symbol, bool signal = true)
    {
        var end = this.Time.Now.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index => new TradingBar(
                end - (51 - index) * 300,
                100,
                index == 50 && signal ? 111 : 101,
                99,
                index == 50 && signal ? 110 : 100,
                index == 50 && signal ? 2 : 1
            ))
            .ToArray();
        return new(symbol, this.Time.Now, signal ? 109.9m : 100m, signal ? 110m : 100.1m, 1000, 1000, bars);
    }

    public void Dispose() => this.Gate.Dispose();
}
