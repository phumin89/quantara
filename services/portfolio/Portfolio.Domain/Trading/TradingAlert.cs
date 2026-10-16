namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingAlert(
    string Key,
    string Kind,
    string Text,
    DateTimeOffset CreatedAt,
    string Status = "pending"
);
