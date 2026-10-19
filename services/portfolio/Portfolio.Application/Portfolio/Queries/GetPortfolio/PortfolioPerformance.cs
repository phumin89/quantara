namespace Quantara.Portfolio.Application;

public sealed record PortfolioPerformance(
    decimal? RealizedPnl,
    decimal? UnrealizedPnl,
    decimal? TotalPnl,
    decimal? Fees,
    DateTimeOffset? AsOf,
    string[] UnreconciledSymbols,
    string Notice
)
{
    public string Scope { get; } = "confirmedEngineFills";
}
