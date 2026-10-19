using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class TradingStateWriter(ITradingStore store)
{
    internal async Task<TradingState> Save(TradingState before, TradingState next, CancellationToken cancellationToken)
    {
        next = next with { Revision = before.Revision + 1 };
        if (!await store.Save(next, before.Revision, cancellationToken))
        {
            throw new InvalidOperationException("Trading state changed concurrently; no new side effect authorized.");
        }

        return next;
    }
}
