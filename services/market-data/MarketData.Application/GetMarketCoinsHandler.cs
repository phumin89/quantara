using Mediator;

namespace Quantara.MarketData.Application;

public sealed class GetMarketCoinsHandler(IMarketDataReader reader) : IQueryHandler<GetMarketCoinsQuery, IReadOnlyList<MarketCoinResult>>
{
    public async ValueTask<IReadOnlyList<MarketCoinResult>> Handle(GetMarketCoinsQuery query, CancellationToken cancellationToken)
    {
        var coins = await reader.ReadCoins(cancellationToken);
        return coins.Select(coin => new MarketCoinResult(coin.Symbol, coin.Name, coin.Source)).ToArray();
    }
}
