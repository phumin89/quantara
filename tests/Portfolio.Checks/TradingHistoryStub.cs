using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class TradingHistoryStub : ITradingHistoryReader
{
    public int Calls { get; private set; }

    public bool Unavailable { get; set; }

    public Task<IReadOnlyList<TradingBar>> Read(
        string symbol,
        int timeframeSeconds,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken
    )
    {
        this.Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        if (this.Unavailable)
        {
            throw new InvalidDataException("Unavailable fixture history.");
        }

        return Task.FromResult<IReadOnlyList<TradingBar>>(
            Enumerable
                .Range(0, (int)((until - from).TotalSeconds / timeframeSeconds))
                .Select(index => new TradingBar(
                    from.ToUnixTimeSeconds() + index * timeframeSeconds,
                    100,
                    101,
                    99,
                    100,
                    1
                ))
                .ToArray()
        );
    }
}
