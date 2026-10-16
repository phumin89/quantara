using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakeTradingDeliveryStore(TradingState state) : ITradingStore
{
    public TradingState State { get; set; } = state;

    public Func<TradingState, bool>? RejectSave { get; set; }

    public Task<TradingState> Read(CancellationToken cancellationToken) => Task.FromResult(this.State);

    public Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        if (this.State.Revision != expectedRevision || this.RejectSave?.Invoke(state) == true)
        {
            return Task.FromResult(false);
        }

        if (state.Revision != expectedRevision + 1)
        {
            throw new InvalidOperationException("Fixture revision must advance exactly once.");
        }

        this.State = state;
        return Task.FromResult(true);
    }
}
