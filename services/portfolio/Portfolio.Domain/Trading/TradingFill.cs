namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingFill(
    Guid Id,
    Guid IntentId,
    string BrokerFillId,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    decimal GrossThb,
    decimal FeeThb,
    DateTimeOffset OccurredAt,
    decimal? RealizedGrossThb = null,
    decimal? RealizedNetThb = null,
    decimal? GrossFeeThb = null,
    decimal? FeeCreditThb = null
);
