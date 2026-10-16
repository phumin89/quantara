using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingStore : IReadOnlyTradingStore
{
    public Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken);
}
