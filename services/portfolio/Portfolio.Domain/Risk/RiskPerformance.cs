namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskPerformance(
    DateTimeOffset From,
    DateTimeOffset To,
    decimal? NetEquityChangeThb,
    decimal? TimeWeightedReturn,
    string Explanation
);
