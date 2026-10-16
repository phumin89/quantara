namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingIntent(
    Guid Id,
    string Symbol,
    TradeSide Side,
    decimal Amount,
    decimal LimitPrice,
    decimal Remaining,
    DateTimeOffset CreatedAt,
    long Candle,
    string RulesHash,
    string Reason,
    string Status,
    string? BrokerOrderId = null,
    DateTimeOffset? LastFillObservation = null,
    string? CancellationState = null,
    DateTimeOffset? LastInspectedAt = null
);
