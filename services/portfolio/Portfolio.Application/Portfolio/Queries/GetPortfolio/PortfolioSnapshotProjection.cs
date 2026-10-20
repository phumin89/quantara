using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public static class PortfolioSnapshotProjection
{
    public static PortfolioLedger Current(
        PortfolioLedger ledger,
        TradingState state,
        string? accountKey,
        DateTimeOffset now
    )
    {
        var symbols = state
            .Live.Fills.Select(fill => fill.Symbol)
            .Concat(state.Live.Positions.Select(item => item.Symbol));
        ledger = ledger with
        {
            Coins = ledger
                .Coins.Concat(symbols.Distinct(StringComparer.Ordinal).Select(symbol => new Coin(symbol, symbol)))
                .DistinctBy(coin => coin.Symbol, StringComparer.Ordinal)
                .ToArray(),
        };
        var capital = state.Autonomy?.Capital;
        if (
            accountKey is null
            || capital is null
            || capital.Source != CapitalSource.Broker
            || capital.AccountKey != accountKey
            || capital.Blocker is not null
            || capital.HasUncertainOrders
            || capital.ObservedAt > now
            || now - capital.ObservedAt > TimeSpan.FromSeconds(state.Rules.MaxDataAgeSeconds)
            || ledger.UpdatedAt is { } previous && capital.ObservedAt <= previous
            || capital.Assets.Count == 0
            || capital.Assets.Select(asset => asset.Symbol).Distinct(StringComparer.Ordinal).Count()
                != capital.Assets.Count
            || capital.Assets.Any(asset =>
                asset.Quantity < 0 || asset.Available < 0 || asset.Available > asset.Quantity
            )
            || !capital.Assets.Any(asset => asset.Symbol == "THB" && asset.Available is not null)
        )
        {
            return ledger;
        }

        return ledger with
        {
            Coins = ledger
                .Coins.Concat(
                    capital
                        .Assets.Where(asset => asset.Symbol != "THB" && asset.Quantity > 0)
                        .Select(asset => new Coin(asset.Symbol, asset.Symbol))
                )
                .DistinctBy(coin => coin.Symbol, StringComparer.Ordinal)
                .ToArray(),
            Balances = capital
                .Assets.Select(asset => new Balance(asset.Symbol, asset.Quantity, asset.Available))
                .ToArray(),
            Prices = capital
                .Assets.Where(asset => asset.Symbol != "THB" && asset.PriceThb is > 0)
                .Select(asset => new Quote(asset.Symbol, asset.PriceThb.GetValueOrDefault()))
                .ToArray(),
            UpdatedAt = capital.ObservedAt,
            PriceUpdatedAt = capital.ObservedAt,
        };
    }
}
