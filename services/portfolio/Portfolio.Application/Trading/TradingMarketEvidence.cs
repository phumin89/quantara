using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed record TradingMarketEvidence(
    IReadOnlyList<TradingBook> Books,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyList<TradingHistoryEvidence>? History = null
);
