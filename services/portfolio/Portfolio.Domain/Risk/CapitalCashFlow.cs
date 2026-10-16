namespace Quantara.Portfolio.Domain.Risk;

public sealed record CapitalCashFlow(
    string Id,
    string Symbol,
    decimal Quantity,
    decimal FeeQuantity,
    DateTimeOffset At,
    CashFlowKind Kind,
    decimal? ValueThb
);
