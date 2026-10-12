namespace Quantara.Portfolio.Domain;

public sealed record Trade(
    string TransactionId,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    decimal FeeCredit,
    DateTimeOffset Timestamp
);
