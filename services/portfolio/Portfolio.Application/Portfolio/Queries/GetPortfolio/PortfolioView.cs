using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed record PortfolioView(
    Position[] Positions,
    PortfolioSummary Summary,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PriceUpdatedAt,
    DateTimeOffset? HistoryUpdatedAt,
    string HistoryCoverage,
    bool Syncing,
    int CompletedSymbols,
    int TotalSymbols,
    string? Error
)
{
    public PortfolioPerformance? Performance { get; init; }
}
