using System.Threading.Channels;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class RealtimeWorkerEventSource : ITradingEventSource
{
    private readonly Channel<TradingMarketEvent> channel = Channel.CreateBounded<TradingMarketEvent>(4);

    public TaskCompletionSource Required { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Publish(TradingMarketEvent item)
    {
        if (!this.channel.Writer.TryWrite(item))
        {
            throw new InvalidOperationException("Fixture queue is full.");
        }
    }

    public async IAsyncEnumerable<TradingMarketEvent> Read(
        IReadOnlyList<string> required,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (required.Contains("ETH", StringComparer.Ordinal))
        {
            this.Required.TrySetResult();
        }

        await foreach (var item in this.channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
