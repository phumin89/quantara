using Quantara.MarketData.Application;
using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Checks;

public sealed class UnusedIntradayCandleStore : IIntradayCandleStore
{
    public Task Initialize(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Watch refresh must not initialize candle storage.");

    public Task Save(string symbol, IReadOnlyList<MarketCandle> candles, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Watch refresh must not write candles.");

    public Task<IReadOnlyList<MarketCandle>> Read(string symbol, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Watch refresh must not read candle storage.");
}
