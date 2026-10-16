using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingMarketReader
{
    public Task<IReadOnlyList<TradingBook>> Read(IReadOnlyList<string> symbols, CancellationToken cancellationToken);

    public Task<IReadOnlyList<TradingBook>> ReadValuations(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => this.Read(symbols, cancellationToken);

    public Task<TradingUniverse> ReadUniverse(CancellationToken cancellationToken) =>
        Task.FromResult(new TradingUniverse(DateTimeOffset.MinValue, [], 0, 0, "Universe reader unavailable."));
}
