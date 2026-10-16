namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingEquityMark(DateTimeOffset At, decimal? EquityThb, decimal FundingThb);
