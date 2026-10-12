namespace Quantara.Portfolio.Domain;

public sealed record Position(string Symbol, string Name, string Status, decimal Quantity, decimal? AverageCost, decimal? CurrentPrice, decimal? Value, decimal? UnrealizedPnl, decimal? RealizedPnl, string CostStatus);
