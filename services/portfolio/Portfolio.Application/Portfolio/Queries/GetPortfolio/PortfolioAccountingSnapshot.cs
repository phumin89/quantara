using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record PortfolioAccountingSnapshot(Position[] Positions, PortfolioPerformance Performance);
