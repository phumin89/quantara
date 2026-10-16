namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingLedger(
    decimal FundingThb,
    decimal CashThb,
    DateTimeOffset OpenedAt,
    IReadOnlyList<TradingHolding> Positions,
    IReadOnlyList<TradingIntent> Orders,
    IReadOnlyList<TradingFill> Fills,
    IReadOnlyList<TradingEquityMark> Equity
);
