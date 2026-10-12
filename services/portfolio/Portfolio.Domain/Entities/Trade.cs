namespace Quantara.Portfolio.Domain;

public sealed record Trade(string TransactionId, string Symbol, string Side, decimal Quantity, decimal Price, decimal Fee, decimal FeeCredit, DateTimeOffset Timestamp);
