using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakeTradingStore(TradingState state) : ITradingStore
{
    public TradingState State { get; set; } = state;

    public List<TradingState> History { get; } = [state];

    public Action<TradingState>? BeforeSave { get; set; }

    public Task<TradingState> Read(CancellationToken cancellationToken) => Task.FromResult(this.State);

    public Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        if (this.State.Revision != expectedRevision)
        {
            return Task.FromResult(false);
        }

        this.BeforeSave?.Invoke(state);
        this.State = state;
        this.History.Add(state);
        return Task.FromResult(true);
    }
}
