using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class TradingTestRepository(TradingState state) : IReadOnlyTradingStore
{
    public Task<TradingState> Read(CancellationToken cancellationToken) => Task.FromResult(state);
}
