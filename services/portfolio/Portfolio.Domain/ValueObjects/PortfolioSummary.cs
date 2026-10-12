namespace Quantara.Portfolio.Domain;

public sealed record PortfolioSummary(decimal? TotalAssets, decimal? AvailableThb, int? HoldingCount);
