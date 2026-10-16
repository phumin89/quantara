namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingBar(long Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
