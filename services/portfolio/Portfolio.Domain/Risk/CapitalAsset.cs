namespace Quantara.Portfolio.Domain.Risk;

public sealed record CapitalAsset(
    string Symbol,
    decimal Quantity,
    decimal? Available,
    decimal? PriceThb,
    decimal PendingSellQuantity,
    decimal CommittedBuyThb = 0
);
