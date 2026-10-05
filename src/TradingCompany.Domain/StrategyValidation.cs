namespace TradingCompany.Domain;

public enum StrategyKind { Cash, SmaTrend, Breakout, MeanReversion, TrendRegimeTrailing }

public sealed record StrategyCostModel
{
    public decimal FeeRate { get; }
    public decimal SlippageRate { get; }
    public StrategyCostModel(decimal feeRate, decimal slippageRate)
    {
        if (feeRate is < 0m or > .10m) throw new ArgumentOutOfRangeException(nameof(feeRate));
        if (slippageRate is < 0m or > .10m) throw new ArgumentOutOfRangeException(nameof(slippageRate));
        FeeRate = feeRate;
        SlippageRate = slippageRate;
    }
}

public sealed record StrategySimulation(
    StrategyKind Strategy,
    decimal InitialCapital,
    decimal FinalEquity,
    decimal NetReturn,
    decimal MaxDrawdown,
    IReadOnlyList<BacktestTrade> Trades,
    int CompletedRoundTrips);

public sealed record WalkForwardFold(
    DateOnly TrainingEnd,
    DateOnly ValidationStart,
    DateOnly ValidationEnd,
    IReadOnlyList<StrategySimulation> Simulations);

public sealed record StrategyValidationEvidence(
    StrategyKind Strategy,
    bool Promoted,
    decimal OutOfSampleReturn,
    decimal MaxDrawdownPercent,
    int CompletedRoundTrips,
    decimal ReturnToDrawdown,
    string? RejectionReason);

public sealed record StrategySelection(
    StrategyKind SelectedStrategy,
    bool Promoted,
    StrategyValidationEvidence? SelectedEvidence,
    IReadOnlyList<StrategyValidationEvidence> Candidates,
    IReadOnlyList<WalkForwardFold> Folds,
    decimal CashBenchmarkReturn,
    decimal BuyAndHoldBenchmarkReturn,
    StrategyCostModel Costs);

public interface IDeterministicStrategy
{
    StrategyKind Kind { get; }
    int RequiredWarmupBars => 0;
    StrategySimulation Run(IReadOnlyList<HistoricalBar> bars, decimal initialCapital, StrategyCostModel costs);
}

public sealed class SmaTrendStrategy : DeterministicLongStrategy
{
    public override StrategyKind Kind => StrategyKind.SmaTrend;
    protected override int WarmupBars => 50;
    protected override bool ShouldEnter(IReadOnlyList<HistoricalBar> bars, int index) => Average(bars, index, 20) > Average(bars, index, 50);
    protected override bool ShouldExit(IReadOnlyList<HistoricalBar> bars, int index) => Average(bars, index, 20) < Average(bars, index, 50);
}

public sealed class BreakoutStrategy : DeterministicLongStrategy
{
    public override StrategyKind Kind => StrategyKind.Breakout;
    protected override int WarmupBars => 20;
    protected override bool ShouldEnter(IReadOnlyList<HistoricalBar> bars, int index)
        => bars[index].Close > bars.Skip(index - 20).Take(20).Max(x => x.Close);
    protected override bool ShouldExit(IReadOnlyList<HistoricalBar> bars, int index)
        => bars[index].Close < bars.Skip(index - 10).Take(10).Min(x => x.Close);
}

public sealed class MeanReversionStrategy : DeterministicLongStrategy
{
    public override StrategyKind Kind => StrategyKind.MeanReversion;
    protected override int WarmupBars => 20;
    protected override bool ShouldEnter(IReadOnlyList<HistoricalBar> bars, int index) => bars[index].Close < Average(bars, index, 20) * .95m;
    protected override bool ShouldExit(IReadOnlyList<HistoricalBar> bars, int index) => bars[index].Close >= Average(bars, index, 20);
}

public sealed class TrendRegimeTrailingStrategy : DeterministicLongStrategy
{
    public override StrategyKind Kind => StrategyKind.TrendRegimeTrailing;
    protected override int WarmupBars => 50;
    protected override decimal Allocation => .75m;

    protected override bool ShouldEnter(IReadOnlyList<HistoricalBar> bars, int index)
        => Average(bars, index, 20) > Average(bars, index, 50) &&
           bars[index].Close > bars.Skip(index - 20).Take(20).Max(bar => bar.Close);

    protected override bool ShouldExit(IReadOnlyList<HistoricalBar> bars, int index)
        => bars[index].Close < Average(bars, index, 20) ||
           bars[index].Close <= bars.Skip(index - 20).Take(20).Max(bar => bar.Close) * .90m;
}

public abstract class DeterministicLongStrategy : IDeterministicStrategy
{
    public abstract StrategyKind Kind { get; }
    public int RequiredWarmupBars => WarmupBars;
    protected abstract int WarmupBars { get; }
    protected virtual decimal Allocation => .25m;
    protected abstract bool ShouldEnter(IReadOnlyList<HistoricalBar> bars, int index);
    protected abstract bool ShouldExit(IReadOnlyList<HistoricalBar> bars, int index);

    public StrategySimulation Run(IReadOnlyList<HistoricalBar> bars, decimal initialCapital, StrategyCostModel costs)
    {
        if (initialCapital <= 0m) throw new ArgumentOutOfRangeException(nameof(initialCapital));
        if (bars.Count == 0) throw new ArgumentException("Historical bars are required.", nameof(bars));
        var ordered = bars.OrderBy(x => x.Date).ToArray();
        if (ordered.Select(x => x.Symbol).Distinct().Count() != 1 || ordered.Zip(ordered.Skip(1)).Any(x => x.First.Date >= x.Second.Date))
            throw new ArgumentException("Historical bars must contain one symbol with unique ascending dates.", nameof(bars));

        decimal cash = initialCapital, position = 0m, costBasis = 0m, peak = initialCapital, maxDrawdown = 0m;
        var completed = 0;
        var trades = new List<BacktestTrade>();
        for (var index = WarmupBars; index < ordered.Length; index++)
        {
            var bar = ordered[index];
            if (position == 0m && ShouldEnter(ordered, index))
            {
                var executionPrice = bar.Close * (1m + costs.SlippageRate);
                var quantity = TradeQuantity.FloorFor(bar.Symbol, (cash * Allocation) / (executionPrice * (1m + costs.FeeRate)));
                var notional = quantity * executionPrice;
                var fee = notional * costs.FeeRate;
                if (quantity > 0m && notional + fee <= cash)
                {
                    cash -= notional + fee;
                    position = quantity;
                    costBasis = notional + fee;
                    trades.Add(new(bar.Date, OrderSide.Buy, quantity, executionPrice, 0m));
                }
            }
            else if (position > 0m && ShouldExit(ordered, index))
            {
                Sell(bar, costs, ref cash, ref position, ref costBasis, trades, ref completed);
            }
            Mark(bar.Close);
        }

        if (position > 0m) Sell(ordered[^1], costs, ref cash, ref position, ref costBasis, trades, ref completed);
        var finalEquity = cash;
        return new(Kind, initialCapital, finalEquity, finalEquity / initialCapital - 1m, maxDrawdown, trades, completed);

        void Mark(decimal close)
        {
            var equity = cash + position * close;
            peak = Math.Max(peak, equity);
            maxDrawdown = Math.Max(maxDrawdown, peak - equity);
        }
    }

    protected static decimal Average(IReadOnlyList<HistoricalBar> bars, int index, int period)
        => bars.Skip(index - period).Take(period).Average(x => x.Close);

    private static void Sell(HistoricalBar bar, StrategyCostModel costs, ref decimal cash, ref decimal position, ref decimal costBasis, List<BacktestTrade> trades, ref int completed)
    {
        var executionPrice = bar.Close * (1m - costs.SlippageRate);
        var proceeds = position * executionPrice;
        var fee = proceeds * costs.FeeRate;
        var pnl = proceeds - fee - costBasis;
        cash += proceeds - fee;
        trades.Add(new(bar.Date, OrderSide.Sell, position, executionPrice, pnl));
        position = 0m;
        costBasis = 0m;
        completed++;
    }
}

public sealed class WalkForwardStrategySelector
{
    private const int MinimumTrainingBars = 120;
    private const int ValidationBars = 60;
    private readonly IReadOnlyList<IDeterministicStrategy> strategies;

    public WalkForwardStrategySelector(IReadOnlyList<IDeterministicStrategy>? strategies = null)
        => this.strategies = strategies ?? [new SmaTrendStrategy(), new BreakoutStrategy(), new MeanReversionStrategy(), new TrendRegimeTrailingStrategy()];

    public static StrategySelection Cash(string reason, StrategyCostModel costs)
    {
        var evidence = new StrategyValidationEvidence(StrategyKind.Cash, false, 0m, 0m, 0, 0m, reason);
        return new(StrategyKind.Cash, false, null, [evidence], [], 0m, 0m, costs);
    }

    public StrategySelection Select(IReadOnlyList<HistoricalBar> source, Symbol symbol, decimal initialCapital, StrategyCostModel costs)
    {
        if (initialCapital <= 0m) throw new ArgumentOutOfRangeException(nameof(initialCapital));
        if (strategies.Count == 0 || strategies.Select(x => x.Kind).Distinct().Count() != strategies.Count) throw new ArgumentException("Strategy candidates must be non-empty and unique.", nameof(strategies));
        var bars = source.Where(x => x.Symbol == symbol).OrderBy(x => x.Date).ToArray();
        if (bars.Length < MinimumTrainingBars + ValidationBars) throw new ArgumentException($"At least {MinimumTrainingBars + ValidationBars} bars are required.", nameof(source));
        if (bars.Zip(bars.Skip(1)).Any(x => x.First.Date >= x.Second.Date)) throw new ArgumentException("Bars must have unique ascending dates.", nameof(source));

        var folds = new List<WalkForwardFold>();
        for (var validationStart = MinimumTrainingBars; validationStart + ValidationBars <= bars.Length; validationStart += ValidationBars)
        {
            var validation = bars.Skip(validationStart).Take(ValidationBars).ToArray();
            var simulations = strategies.Select(strategy =>
            {
                if (strategy.RequiredWarmupBars < 0 || strategy.RequiredWarmupBars > validationStart)
                    throw new ArgumentOutOfRangeException(nameof(strategies), "Strategy warmup must fit inside the available training history.");
                var evaluationBars = bars
                    .Skip(validationStart - strategy.RequiredWarmupBars)
                    .Take(strategy.RequiredWarmupBars + ValidationBars)
                    .ToArray();
                return strategy.Run(evaluationBars, initialCapital, costs);
            }).ToArray();
            folds.Add(new(bars[validationStart - 1].Date, validation[0].Date, validation[^1].Date, simulations));
        }

        var buyAndHold = folds.Aggregate(1m, (growth, fold) =>
        {
            var firstDate = fold.ValidationStart;
            var lastDate = fold.ValidationEnd;
            var first = bars.Single(x => x.Date == firstDate).Close;
            var last = bars.Single(x => x.Date == lastDate).Close;
            return growth * (last / first);
        }) - 1m;
        var candidates = strategies.Select(strategy => Aggregate(strategy.Kind, folds, initialCapital)).ToArray();
        var selected = candidates.Where(x => x.Promoted)
            .OrderByDescending(x => x.ReturnToDrawdown)
            .ThenByDescending(x => x.OutOfSampleReturn)
            .ThenBy(x => x.Strategy)
            .FirstOrDefault();
        return new(selected?.Strategy ?? StrategyKind.Cash, selected is not null, selected, candidates, folds, 0m, buyAndHold, costs);
    }

    private static StrategyValidationEvidence Aggregate(StrategyKind kind, IReadOnlyList<WalkForwardFold> folds, decimal initialCapital)
    {
        var simulations = folds.SelectMany(x => x.Simulations).Where(x => x.Strategy == kind).ToArray();
        var netReturn = simulations.Aggregate(1m, (growth, result) => growth * (1m + result.NetReturn)) - 1m;
        var maxDrawdownPercent = simulations.Max(x => x.MaxDrawdown / initialCapital);
        var roundTrips = simulations.Sum(x => x.CompletedRoundTrips);
        var rejection = netReturn <= 0m ? "Positive net return required."
            : maxDrawdownPercent > .10m ? "Maximum drawdown exceeds 10%."
            : roundTrips < 2 ? "At least two completed round trips required."
            : null;
        var score = netReturn / Math.Max(maxDrawdownPercent, .0001m);
        return new(kind, rejection is null, netReturn, maxDrawdownPercent, roundTrips, score, rejection);
    }
}
