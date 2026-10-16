namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingShadowSummary(
    string Date,
    int Captured,
    int Closed,
    int Waiting,
    int Unknown,
    decimal? MeanNetReturnFraction
);
