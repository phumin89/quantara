using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public interface ITradingMarketReader
{
    public Task<IReadOnlyList<TradingBook>> Read(IReadOnlyList<string> symbols, CancellationToken cancellationToken);

    public async Task<TradingMarketEvidence> ReadEvidence(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken,
        bool adaptiveTimeframes = false
    ) => new(await this.Read(symbols, cancellationToken), new Dictionary<string, string>());

    public Task<IReadOnlyList<TradingBook>> ReadValuations(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    ) => this.Read(symbols, cancellationToken);

    public Task<TradingUniverse> ReadUniverse(CancellationToken cancellationToken) =>
        Task.FromResult(new TradingUniverse(DateTimeOffset.MinValue, [], 0, 0, "Universe reader unavailable."));
}
