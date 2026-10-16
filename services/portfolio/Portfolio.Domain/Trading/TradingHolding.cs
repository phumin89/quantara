namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingHolding(
    string Symbol,
    decimal Quantity,
    decimal CostThb,
    DateTimeOffset OpenedAt,
    decimal PeakBid,
    decimal EntryFeesThb = 0
)
{
    public decimal AverageCost => this.Quantity == 0m ? 0m : this.CostThb / this.Quantity;

    public decimal? StopPrice { get; init; }

    public decimal? TargetPrice { get; init; }

    public decimal? TrailingDistance { get; init; }
}
