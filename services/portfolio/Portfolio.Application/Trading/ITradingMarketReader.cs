using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingMarketReader
{
    public Task<IReadOnlyList<TradingBook>> Read(IReadOnlyList<string> symbols, CancellationToken cancellationToken);
}
