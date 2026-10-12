namespace Quantara.Portfolio.Domain;

public sealed record Position(
    string Symbol,
    string Name,
    PositionStatus Status,
    decimal Quantity,
    decimal? AverageCost,
    decimal? CurrentPrice,
    decimal? Value,
    decimal? UnrealizedPnl,
    decimal? RealizedPnl,
    CostStatus CostStatus
);
