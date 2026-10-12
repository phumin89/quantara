using Mediator;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Application;

public sealed class GetPortfolioHandler(IReadOnlyPortfolioRepository store, IPortfolioSync sync)
    : IQueryHandler<GetPortfolioQuery, PortfolioView>
{
    public async ValueTask<PortfolioView> Handle(GetPortfolioQuery query, CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        var status = sync.Status;
        return new(
            PortfolioProjection.Positions(ledger),
            PortfolioProjection.Summary(ledger),
            ledger.UpdatedAt,
            ledger.PriceUpdatedAt,
            ledger.HistoryUpdatedAt,
            HistoryCoverage.Notice,
            status.Syncing,
            status.CompletedSymbols,
            status.TotalSymbols,
            status.Error
        );
    }
}
