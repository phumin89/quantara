using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public sealed class FakeTradingStore(TradingState state) : ITradingStore
{
    public TradingState State { get; set; } = state;

    public List<TradingState> History { get; } = [state];

    public Action<TradingState>? BeforeSave { get; set; }

    public Action<TradingState>? AfterSave { get; set; }

    public Task<TradingState> Read(CancellationToken cancellationToken) => Task.FromResult(this.State);

    public Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        this.BeforeSave?.Invoke(state);
        if (this.State.Revision != expectedRevision)
        {
            return Task.FromResult(false);
        }

        foreach (var (previous, next) in new[] { (this.State.Paper, state.Paper), (this.State.Live, state.Live) })
        {
            var retained = previous.Orders.ToDictionary(order => order.Id);
            foreach (var intent in next.Orders)
            {
                if (retained.TryGetValue(intent.Id, out var old))
                {
                    TradingIntentPersistence.Validate(old, intent);
                }
            }
        }

        TradingPolicyPersistence.Validate(this.State, state);

        this.State = state;
        this.History.Add(state);
        this.AfterSave?.Invoke(state);
        return Task.FromResult(true);
    }
}
