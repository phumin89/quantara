namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingDecision(TradingAction Action, string Reason, long Candle, decimal ReferencePrice);
