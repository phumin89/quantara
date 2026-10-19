namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingExitAssessment(TradingExitPlan Plan, TradingAction Action, string Reason);
