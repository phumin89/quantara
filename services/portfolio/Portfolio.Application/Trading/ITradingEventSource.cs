using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingEventSource
{
    public IAsyncEnumerable<TradingMarketEvent> Read(
        IReadOnlyList<string> required,
        CancellationToken cancellationToken
    );
}
