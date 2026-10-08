using TradingCompany.Domain;

namespace TradingCompany.Application;

// Historical comparisons share validation windows and costs. They are not expected future returns.
public sealed record AiCompanyStrategyAlternative(string Strategy, bool Promoted, decimal NetReturn,
    decimal MaxDrawdown, int CompletedRoundTrips, string? RejectionReason, IReadOnlyList<decimal> FoldNetReturns);

public sealed record AiCompanyOpportunityEvidence(DateOnly ValidationStart, DateOnly ValidationEnd, int FoldCount,
    decimal CashBenchmarkReturn, decimal NetBuyAndHoldBenchmarkReturn, decimal HistoricalCashOpportunityCost,
    StrategyCostModel Costs, IReadOnlyList<AiCompanyStrategyAlternative> Strategies)
{
    public static AiCompanyOpportunityEvidence From(StrategySelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Folds.Count == 0) throw new ArgumentException("Validation folds are required.", nameof(selection));
        return new(selection.Folds[0].ValidationStart, selection.Folds[^1].ValidationEnd, selection.Folds.Count,
            selection.CashBenchmarkReturn, selection.BuyAndHoldBenchmarkReturn,
            selection.Candidates.Where(candidate => candidate.Promoted)
                .Select(candidate => candidate.OutOfSampleReturn - selection.CashBenchmarkReturn).Append(0m).Max(),
            selection.Costs, selection.Candidates.Select(candidate => new AiCompanyStrategyAlternative(
                candidate.Strategy.ToString(), candidate.Promoted, candidate.OutOfSampleReturn,
                candidate.MaxDrawdownPercent, candidate.CompletedRoundTrips, candidate.RejectionReason,
                selection.Folds.Select(fold => fold.Simulations.Single(s => s.Strategy == candidate.Strategy).NetReturn).ToArray())).ToArray());
    }
}
