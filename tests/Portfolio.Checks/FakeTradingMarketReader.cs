using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakeTradingMarketReader(IReadOnlyList<TradingBook> books) : ITradingMarketReader
{
    public IReadOnlyList<TradingBook> Books { get; set; } = books;

    public Action? BeforeRead { get; set; }

    public TradingUniverse? Universe { get; set; }

    public Action? BeforeUniverseRead { get; set; }

    public Task<TradingUniverse> ReadUniverse(CancellationToken cancellationToken)
    {
        this.BeforeUniverseRead?.Invoke();
        return Task.FromResult(
            this.Universe ?? new TradingUniverse(DateTimeOffset.MinValue, [], 0, 0, "Not configured.")
        );
    }

    public Task<IReadOnlyList<TradingBook>> Read(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        this.BeforeRead?.Invoke();
        return Task.FromResult<IReadOnlyList<TradingBook>>(
            this.Books.Where(book => symbols.Contains(book.Symbol, StringComparer.Ordinal)).ToArray()
        );
    }
}
