using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record SnapshotFxRate(DateOnly Date, decimal Rate);
public sealed record HistoricalMarketEvidence(
    IReadOnlyList<HistoricalBar> Bars,
    IReadOnlyList<HistoricalBar> SourceBars,
    IReadOnlyList<SnapshotFxRate> FxRates,
    string Source,
    string SourceCurrency);

public interface IHistoricalMarketEvidenceProvider : IHistoricalMarketDataProvider
{
    Task<HistoricalMarketEvidence> GetEvidenceAsync(Symbol symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken);
}
