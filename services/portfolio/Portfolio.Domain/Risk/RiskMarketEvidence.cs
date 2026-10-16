namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskMarketEvidence(
    string Symbol,
    DateTimeOffset ObservedAt,
    decimal Bid,
    decimal Ask,
    decimal BuyCapacityThb,
    decimal SellCapacityQuantity,
    decimal Atr,
    decimal MinimumNotional,
    decimal PriceStep,
    int PriceScale,
    int AmountScale,
    bool Enabled,
    bool StopVerified
);
