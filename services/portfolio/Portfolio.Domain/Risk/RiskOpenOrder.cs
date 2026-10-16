namespace Quantara.Portfolio.Domain.Risk;

public sealed record RiskOpenOrder(string Id, string Symbol, TradeSide Side, decimal Remaining, decimal LimitPrice);
