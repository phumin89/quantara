using Mediator;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class GetPortfolioHandler(
    IReadOnlyPortfolioRepository store,
    IReadOnlyPortfolioSync sync,
    IReadOnlyTradingStore? trading = null,
    TimeProvider? time = null,
    IAccountContext? account = null
) : IQueryHandler<GetPortfolioQuery, PortfolioView>
{
    public async ValueTask<PortfolioView> Handle(GetPortfolioQuery query, CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        var status = sync.Status;
        var positions = PortfolioProjection.Positions(ledger);
        PortfolioPerformance? performance = null;
        if (trading is not null)
        {
            var state = await trading.Read(cancellationToken);
            var now = (time ?? TimeProvider.System).GetUtcNow();
            ledger = PortfolioSnapshotProjection.Current(ledger, state, account?.AccountKey, now);
            var accounting = PortfolioAccountingProjection.Build(ledger, state.Live, now);
            positions = accounting.Positions;
            performance = accounting.Performance;
            var latestBuy = state
                .Live.Fills.Where(fill => fill.Side == TradeSide.Buy && fill.Quantity > 0 && fill.GrossThb > 0)
                .GroupBy(fill => new { fill.Symbol, fill.IntentId })
                .Select(group => group.OrderByDescending(fill => fill.OccurredAt).ToArray())
                .OrderByDescending(group => group.Max(fill => fill.OccurredAt))
                .GroupBy(group => group[0].Symbol, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var fills = group.First();
                        return (
                            Price: fills.Sum(fill => fill.GrossThb) / fills.Sum(fill => fill.Quantity),
                            At: fills.Max(fill => fill.OccurredAt)
                        );
                    },
                    StringComparer.Ordinal
                );
            positions = positions
                .Select(position =>
                    latestBuy.TryGetValue(position.Symbol, out var buy)
                        ? position with
                        {
                            LastBuyPrice = buy.Price,
                            LastBoughtAt = buy.At,
                        }
                        : position
                )
                .ToArray();
        }

        return new(
            positions,
            PortfolioProjection.Summary(ledger),
            ledger.UpdatedAt,
            ledger.PriceUpdatedAt,
            ledger.HistoryUpdatedAt,
            HistoryCoverage.Notice,
            status.Syncing,
            status.CompletedSymbols,
            status.TotalSymbols,
            status.Error
        )
        {
            Performance = performance,
        };
    }
}
