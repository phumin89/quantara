using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Domain.Trading;

public sealed record AutonomousTradingStatus(
    DateTimeOffset ObservedAt,
    DateTimeOffset? UniverseAt,
    int TotalMarkets,
    int EligibleMarkets,
    IReadOnlyList<TradingScanObservation> Markets,
    CapitalSnapshot? Capital,
    string Activity,
    DateTimeOffset NextCheckAt
)
{
    public IReadOnlyList<TradingHistoryEvidence>? History { get; init; }
}
