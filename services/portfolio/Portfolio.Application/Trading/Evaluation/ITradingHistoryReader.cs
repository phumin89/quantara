using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingHistoryReader
{
    public Task<IReadOnlyList<TradingBar>> Read(
        string symbol,
        int timeframeSeconds,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken
    );
}
