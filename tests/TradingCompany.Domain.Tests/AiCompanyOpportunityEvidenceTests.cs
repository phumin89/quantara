using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class AiCompanyOpportunityEvidenceTests
{
    [Theory]
    [InlineData(.04, true)]
    [InlineData(-.04, false)]
    public void Cash_opportunity_cost_uses_validated_net_return_and_retains_losing_alternatives(decimal foldReturn, bool promoted)
    {
        var bars = Enumerable.Range(0, 240).Select(i => new HistoricalBar(new DateOnly(2025, 1, 1).AddDays(i),
            new("BTC-THB"), 100m, 101m, 99m, 100m, 1000)).ToArray();
        var selection = new WalkForwardStrategySelector([new FixedStrategy(foldReturn)])
            .Select(bars, new("BTC-THB"), 100_000m, new(.003m, .01m));
        var comparison = AiCompanyOpportunityEvidence.From(selection);
        var expectedReturn = (1 + foldReturn) * (1 + foldReturn) - 1;
        Assert.Equal(promoted, selection.Promoted);
        Assert.Equal(promoted ? expectedReturn : 0m, comparison.HistoricalCashOpportunityCost);
        Assert.Equal(0m, comparison.CashBenchmarkReturn);
        var alternative = Assert.Single(comparison.Strategies);
        Assert.Equal("SmaTrend", alternative.Strategy);
        Assert.Equal(expectedReturn, alternative.NetReturn);
        Assert.Equal(new[] { foldReturn, foldReturn }, alternative.FoldNetReturns);
        Assert.Equal(promoted, alternative.Promoted);
        Assert.Equal(selection.Candidates[0].RejectionReason, alternative.RejectionReason);
        Assert.Equal(selection.Folds[0].ValidationStart, comparison.ValidationStart);
        Assert.Equal(selection.Folds[^1].ValidationEnd, comparison.ValidationEnd);
        Assert.Equal(2, comparison.FoldCount);
        Assert.Equal(selection.Costs, comparison.Costs);
        Assert.Equal(selection.BuyAndHoldBenchmarkReturn, comparison.NetBuyAndHoldBenchmarkReturn);
        Assert.True(comparison.NetBuyAndHoldBenchmarkReturn < 0m);
    }

    [Fact]
    public void Opportunity_cost_compares_best_validated_return_even_when_risk_adjusted_selection_differs()
    {
        var bars = Enumerable.Range(0, 240).Select(i => new HistoricalBar(new DateOnly(2025, 1, 1).AddDays(i),
            new("BTC-THB"), 100m, 101m, 99m, 100m, 1000)).ToArray();
        var selection = new WalkForwardStrategySelector([
            new FixedStrategy(.20m, StrategyKind.SmaTrend, 2000m),
            new FixedStrategy(.30m, StrategyKind.Breakout, 5000m),
            new FixedStrategy(.40m, StrategyKind.MeanReversion, 11000m)])
            .Select(bars, new("BTC-THB"), 100_000m, new(0m, 0m));
        var comparison = AiCompanyOpportunityEvidence.From(selection);
        Assert.Equal(StrategyKind.SmaTrend, selection.SelectedStrategy);
        Assert.Equal(.69m, comparison.HistoricalCashOpportunityCost);
        Assert.Equal(3, comparison.Strategies.Count);
        Assert.Contains(comparison.Strategies, strategy => strategy.NetReturn == .96m && !strategy.Promoted);
    }

    private sealed class FixedStrategy(decimal netReturn, StrategyKind kind = StrategyKind.SmaTrend, decimal drawdown = 1000m) : IDeterministicStrategy
    {
        public StrategyKind Kind => kind;
        public StrategySimulation Run(IReadOnlyList<HistoricalBar> bars, decimal capital, StrategyCostModel costs)
            => new(Kind, capital, capital * (1 + netReturn), netReturn, drawdown, [], 1);
    }
}
