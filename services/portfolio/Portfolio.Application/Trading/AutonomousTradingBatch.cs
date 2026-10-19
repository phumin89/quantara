using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record AutonomousTradingBatch(
    AutonomousTradingStatus Status,
    IReadOnlyList<TradingBook> Books,
    IReadOnlyList<string> EntrySymbols,
    CapitalReadResult Capital
)
{
    public IReadOnlyList<TradingBook> ExitStudyBooks { get; init; } = [];
}
