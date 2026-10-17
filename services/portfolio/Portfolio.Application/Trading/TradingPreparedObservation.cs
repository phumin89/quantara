using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record TradingPreparedObservation(
    TradingIntent Intent,
    TradingOrderObservation Observation,
    DateTimeOffset ObservedAt
);
