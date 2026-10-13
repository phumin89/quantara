using Quantara.MarketData.Domain;

namespace Quantara.MarketData.Application;

public interface IIntradayCandleStore
{
    public Task Initialize(CancellationToken cancellationToken);

    public Task Save(string symbol, IReadOnlyList<MarketCandle> candles, CancellationToken cancellationToken);

    public Task<IReadOnlyList<MarketCandle>> Read(string symbol, CancellationToken cancellationToken);
}
