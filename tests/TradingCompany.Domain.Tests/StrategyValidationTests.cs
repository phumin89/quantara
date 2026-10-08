using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class StrategyValidationTests
{
    [Fact]
    public void Strategy_costs_reduce_final_equity()
    {
        var bars = RisingBars(120);
        var strategy = new SmaTrendStrategy();

        var free = strategy.Run(bars, 100_000m, new(0m, 0m));
        var costly = strategy.Run(bars, 100_000m, new(.001m, .001m));

        Assert.True(free.CompletedRoundTrips >= 1);
        Assert.True(costly.FinalEquity < free.FinalEquity);
    }

    [Theory]
    [InlineData(StrategyKind.SmaTrend)]
    [InlineData(StrategyKind.Breakout)]
    [InlineData(StrategyKind.MeanReversion)]
    [InlineData(StrategyKind.TrendRegimeTrailing)]
    public void Strategies_hold_cash_without_entry_signal(StrategyKind kind)
    {
        var simulation = Strategy(kind).Run(FlatBars(120), 100_000m, new(.001m, .001m));

        Assert.Equal(100_000m, simulation.FinalEquity);
        Assert.Empty(simulation.Trades);
    }

    [Fact]
    public void Strategy_simulation_is_deterministic()
    {
        var strategy = new BreakoutStrategy();
        var bars = RisingBars(120);

        var first = strategy.Run(bars, 100_000m, new(.001m, .001m));
        var second = strategy.Run(bars, 100_000m, new(.001m, .001m));
        Assert.Equal(first.FinalEquity, second.FinalEquity);
        Assert.Equal(first.MaxDrawdown, second.MaxDrawdown);
        Assert.Equal(first.Trades, second.Trades);
    }

    [Fact]
    public void Trend_regime_candidate_uses_seventy_five_percent_without_leverage()
    {
        var simulation = new TrendRegimeTrailingStrategy().Run(RisingBars(120), 100_000m, new(0m, 0m));

        var buy = simulation.Trades.First(trade => trade.Side == OrderSide.Buy);
        var notional = buy.Quantity * buy.Price;

        Assert.InRange(notional, 74_000m, 75_000m);
        Assert.True(notional <= simulation.InitialCapital);
    }

    [Fact]
    public void Trend_regime_candidate_exits_after_ten_percent_rolling_high_reversal()
    {
        var bars = RisingBars(60).Concat([Bar(60, 140m)]).ToArray();

        var simulation = new TrendRegimeTrailingStrategy().Run(bars, 100_000m, new(0m, 0m));

        Assert.Equal(Bar(60, 140m).Date, simulation.Trades.Last().Date);
        Assert.Equal(OrderSide.Sell, simulation.Trades.Last().Side);
    }

    [Fact]
    public void Trend_regime_candidate_stays_in_cash_in_a_bear_regime()
    {
        var bars = Enumerable.Range(0, 120).Select(index => Bar(index, 300m - index)).ToArray();

        var simulation = new TrendRegimeTrailingStrategy().Run(bars, 100_000m, new(0m, 0m));

        Assert.Empty(simulation.Trades);
        Assert.Equal(100_000m, simulation.FinalEquity);
    }

    [Fact]
    public void Walk_forward_first_fold_does_not_read_future_bars()
    {
        var selector = new WalkForwardStrategySelector();
        var prefix = RisingBars(180);
        var extended = prefix.Concat(Enumerable.Range(180, 60).Select(index => Bar(index, 20m))).ToArray();

        var before = selector.Select(prefix, new("TEST"), 100_000m, new(.001m, .001m));
        var after = selector.Select(extended, new("TEST"), 100_000m, new(.001m, .001m));

        Assert.Equal(before.Folds[0].TrainingEnd, after.Folds[0].TrainingEnd);
        Assert.Equal(before.Folds[0].ValidationStart, after.Folds[0].ValidationStart);
        Assert.Equal(before.Folds[0].ValidationEnd, after.Folds[0].ValidationEnd);
        Assert.Equal(before.Folds[0].Simulations.Select(x => (x.Strategy, x.FinalEquity, x.MaxDrawdown)), after.Folds[0].Simulations.Select(x => (x.Strategy, x.FinalEquity, x.MaxDrawdown)));
    }

    [Fact]
    public void Walk_forward_uses_pre_validation_bars_only_as_signal_warmup()
    {
        var result = new WalkForwardStrategySelector([new SmaTrendStrategy()])
            .Select(RisingBars(180), new("TEST"), 100_000m, new(0m, 0m));

        var fold = Assert.Single(result.Folds);
        var simulation = Assert.Single(fold.Simulations);

        Assert.Equal(fold.ValidationStart, simulation.Trades[0].Date);
        Assert.All(simulation.Trades, trade => Assert.InRange(trade.Date, fold.ValidationStart, fold.ValidationEnd));
    }

    [Fact]
    public void Buy_and_hold_benchmark_has_gross_return_without_costs()
    {
        var result = new WalkForwardStrategySelector([new FixedStrategy(.10m, 0m, 2)])
            .Select(RisingBars(240), new("TEST"), 100_000m, new(0m, 0m));

        var expected = ((279m / 220m) * (339m / 280m)) - 1m;
        Assert.InRange(result.BuyAndHoldBenchmarkReturn, expected - .0000000000000000000001m, expected + .0000000000000000000001m);
    }

    [Fact]
    public void Buy_and_hold_benchmark_compounds_net_costs_per_fold()
    {
        var costs = new StrategyCostModel(.01m, .02m);
        var result = new WalkForwardStrategySelector([new FixedStrategy(.10m, 0m, 2)])
            .Select(RisingBars(240), new("TEST"), 100_000m, costs);

        static decimal Fold(decimal first, decimal last) =>
            (last * (1m - .02m) / (first * (1m + .02m))) * (1m - .01m) / (1m + .01m);
        var expected = Fold(220m, 279m) * Fold(280m, 339m) - 1m;
        Assert.InRange(result.BuyAndHoldBenchmarkReturn, expected - .0000000000000000000001m, expected + .0000000000000000000001m);
    }

    [Fact]
    public void Flat_market_benchmark_is_negative_while_cash_benchmark_is_zero()
    {
        var result = new WalkForwardStrategySelector([new FixedStrategy(.10m, 0m, 2)])
            .Select(FlatBars(240), new("TEST"), 100_000m, new(.001m, .001m));

        Assert.Equal(0m, result.CashBenchmarkReturn);
        Assert.True(result.BuyAndHoldBenchmarkReturn < 0m);
    }

    [Fact]
    public void Benchmark_does_not_change_strategy_outcomes_or_selection()
    {
        var result = new WalkForwardStrategySelector([new FixedStrategy(.10m, 0m, 2)])
            .Select(RisingBars(240), new("TEST"), 100_000m, new(.001m, .001m));

        var evidence = Assert.Single(result.Candidates);
        Assert.Equal(StrategyKind.SmaTrend, result.SelectedStrategy);
        Assert.Equal(.21m, evidence.OutOfSampleReturn);
        Assert.True(result.Promoted);
    }

    [Fact]
    public void Walk_forward_defaults_to_cash_when_no_strategy_passes()
    {
        var result = new WalkForwardStrategySelector().Select(FlatBars(240), new("TEST"), 100_000m, new(.001m, .001m));

        Assert.False(result.Promoted);
        Assert.Equal(StrategyKind.Cash, result.SelectedStrategy);
        Assert.Contains(result.Candidates, candidate => candidate.RejectionReason == "Positive net return required.");
    }

    [Fact]
    public void Walk_forward_rejects_drawdown_above_ten_percent()
    {
        var selector = new WalkForwardStrategySelector([new FixedStrategy(.20m, 11_000m, 3)]);

        var result = selector.Select(RisingBars(240), new("TEST"), 100_000m, new(0m, 0m));

        Assert.False(result.Promoted);
        Assert.Contains(result.Candidates, candidate => candidate.RejectionReason == "Maximum drawdown exceeds 10%.");
    }

    [Fact]
    public void Walk_forward_rejects_fewer_than_two_round_trips()
    {
        var selector = new WalkForwardStrategySelector([new FixedStrategy(.10m, 1_000m, 1)]);

        var result = selector.Select(RisingBars(180), new("TEST"), 100_000m, new(0m, 0m));

        Assert.False(result.Promoted);
        Assert.Contains(result.Candidates, candidate => candidate.RejectionReason == "At least two completed round trips required.");
    }

    private static IDeterministicStrategy Strategy(StrategyKind kind) => kind switch
    {
        StrategyKind.SmaTrend => new SmaTrendStrategy(),
        StrategyKind.Breakout => new BreakoutStrategy(),
        StrategyKind.MeanReversion => new MeanReversionStrategy(),
        StrategyKind.TrendRegimeTrailing => new TrendRegimeTrailingStrategy(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IReadOnlyList<HistoricalBar> RisingBars(int count)
        => Enumerable.Range(0, count).Select(index => Bar(index, 100m + index)).ToArray();

    private static IReadOnlyList<HistoricalBar> FlatBars(int count)
        => Enumerable.Range(0, count).Select(index => Bar(index, 100m)).ToArray();

    private static HistoricalBar Bar(int index, decimal close)
        => new(new DateOnly(2025, 1, 1).AddDays(index), new("TEST"), close, close + 1m, close - 1m, close, 1_000);

    private sealed class FixedStrategy(decimal netReturn, decimal drawdown, int roundTrips) : IDeterministicStrategy
    {
        public StrategyKind Kind => StrategyKind.SmaTrend;
        public StrategySimulation Run(IReadOnlyList<HistoricalBar> bars, decimal initialCapital, StrategyCostModel costs)
            => new(Kind, initialCapital, initialCapital * (1m + netReturn), netReturn, drawdown, [], roundTrips);
    }
}
