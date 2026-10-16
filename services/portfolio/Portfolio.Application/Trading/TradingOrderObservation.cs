using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record TradingOrderObservation(
    string Status,
    string? BrokerOrderId,
    decimal? Remaining,
    IReadOnlyList<TradingFill> Fills,
    string? Error = null
);
