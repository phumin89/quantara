namespace Quantara.Portfolio.Application;

public sealed record OrderMarket(
    decimal ReferencePrice,
    decimal MinimumNotional,
    decimal PriceStep,
    int PriceScale,
    int AmountScale,
    bool Enabled
);
