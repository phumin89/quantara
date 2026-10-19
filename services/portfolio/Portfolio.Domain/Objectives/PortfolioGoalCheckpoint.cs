namespace Quantara.Portfolio.Domain;

public sealed record PortfolioGoalCheckpoint(
    string AccountKey,
    string ObjectiveId,
    DateTimeOffset ObservedAt,
    decimal? PortfolioValueThb,
    decimal? DepositsThb,
    decimal? TradingPnlThb,
    string? Reason
);
