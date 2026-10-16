using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface IReadOnlyTradingStore
{
    public Task<TradingState> Read(CancellationToken cancellationToken);
}
