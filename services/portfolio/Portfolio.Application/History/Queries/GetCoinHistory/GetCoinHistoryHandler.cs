using Mediator;
namespace Quantara.Portfolio.Application;

public sealed class GetCoinHistoryHandler(IReadOnlyPortfolioRepository store) : IQueryHandler<GetCoinHistoryQuery, CoinHistory>
{
    public async ValueTask<CoinHistory> Handle(GetCoinHistoryQuery query, CancellationToken cancellationToken)
    {
        var ledger = await store.Load(cancellationToken);
        return new(ledger.Trades.Where(item => item.Symbol == query.Coin).OrderByDescending(item => item.Timestamp).ToArray(), HistoryCoverage.Notice);
    }
}
