using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class TradingReportCompletionStore(TradingState state) : ITradingStore
{
    private readonly FakeTradingDeliveryStore inner = new(state);

    public TradingState State => this.inner.State;

    public bool Completing { get; set; }

    public bool StallRead { get; set; }

    public bool StallSave { get; set; }

    public bool RejectCompletionSaves { get; set; }

    public Action? BeforeCompletionRead { get; set; }

    public int CompletionReads { get; private set; }

    public int CompletionSaves { get; private set; }

    public List<CancellationToken> CompletionTokens { get; } = [];

    public async Task<TradingState> Read(CancellationToken cancellationToken)
    {
        if (this.Completing)
        {
            this.CompletionReads++;
            this.CompletionTokens.Add(cancellationToken);
            this.BeforeCompletionRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (this.StallRead)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        return await this.inner.Read(cancellationToken);
    }

    public async Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        if (this.Completing)
        {
            this.CompletionSaves++;
            this.CompletionTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (this.StallSave)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (this.RejectCompletionSaves)
            {
                return false;
            }
        }

        return await this.inner.Save(state, expectedRevision, cancellationToken);
    }
}
