namespace Quantara.Portfolio.Domain.Risk;

public sealed record CapitalCashFlow(
    string Id,
    string Symbol,
    decimal Quantity,
    decimal FeeQuantity,
    DateTimeOffset At,
    CashFlowKind Kind,
    // Delivered transfer value, net of native withdrawal fees; unknown without verified valuation.
    decimal? ValueThb
);
