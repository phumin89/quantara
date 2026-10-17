namespace Quantara.MarketData.Application;

public interface IMarketEventBus : IMarketEventReader
{
    public ValueTask SetWatch(IReadOnlyList<string> symbols, CancellationToken cancellationToken);

    public ValueTask SetWatched(IReadOnlyList<string> symbols, CancellationToken cancellationToken);
}
