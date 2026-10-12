namespace Quantara.Portfolio.Domain;

public static class PortfolioProjection
{
    public static PortfolioSummary Summary(PortfolioLedger ledger)
    {
        if (ledger.UpdatedAt is null)
        {
            return new(null, null, null);
        }

        var held = ledger.Balances.Where(item => item.Symbol != "THB" && item.Quantity > 0).ToArray();
        if (held.Length == 0)
        {
            return new(0, ledger.Balances.FirstOrDefault(item => item.Symbol == "THB")?.Available, 0);
        }

        var hasCompletePrices = held.All(item =>
            ledger.Prices.Any(price => price.Symbol == item.Symbol && price.Price > 0)
        );
        var total = hasCompletePrices
            ? held.Sum(item => item.Quantity * ledger.Prices.Single(price => price.Symbol == item.Symbol).Price)
            : (decimal?)null;
        return new(total, ledger.Balances.FirstOrDefault(item => item.Symbol == "THB")?.Available, held.Length);
    }

    public static Position[] Positions(PortfolioLedger ledger) =>
        ledger
            .Coins.Select(coin =>
            {
                var quantity = ledger.Balances.FirstOrDefault(item => item.Symbol == coin.Symbol)?.Quantity ?? 0;
                var price = ledger.Prices.FirstOrDefault(item => item.Symbol == coin.Symbol)?.Price;
                var status = quantity > 0 ? PositionStatus.Holding : PositionStatus.Closed;
                var value = quantity == 0 ? 0 : price * quantity;
                return new Position(
                    coin.Symbol,
                    coin.Name,
                    status,
                    quantity,
                    null,
                    price,
                    value,
                    null,
                    null,
                    CostStatus.Unknown
                );
            })
            .OrderBy(item => item.Status == PositionStatus.Closed)
            .ThenBy(item => item.Symbol)
            .ToArray();
}
