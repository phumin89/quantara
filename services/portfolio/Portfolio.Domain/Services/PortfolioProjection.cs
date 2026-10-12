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
        var total = held.Length == 0 ? 0 : held.All(item => ledger.Prices.Any(price => price.Symbol == item.Symbol && price.Price > 0))
            ? held.Sum(item => item.Quantity * ledger.Prices.Single(price => price.Symbol == item.Symbol).Price) : (decimal?)null;
        return new(total, ledger.Balances.FirstOrDefault(item => item.Symbol == "THB")?.Available, held.Length);
    }
    public static Position[] Positions(PortfolioLedger ledger) => ledger.Coins.Select(coin =>
    {
        var quantity = ledger.Balances.FirstOrDefault(item => item.Symbol == coin.Symbol)?.Quantity ?? 0;
        var price = ledger.Prices.FirstOrDefault(item => item.Symbol == coin.Symbol)?.Price;
        return new Position(coin.Symbol, coin.Name, quantity > 0 ? "Holding" : "Closed", quantity, null, price, quantity == 0 ? 0 : price * quantity, null, null, "Unknown");
    }).OrderBy(item => item.Status == "Closed").ThenBy(item => item.Symbol).ToArray();
}
