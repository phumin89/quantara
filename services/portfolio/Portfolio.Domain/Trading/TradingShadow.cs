namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingShadow(
    string Key,
    string Symbol,
    string StrategyVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int TimeExitMinutes,
    int MaximumDataAgeSeconds,
    decimal LimitPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal TrailingDistance,
    decimal BudgetThb,
    decimal FeeBps,
    int BaseScale,
    TradingShadowState State = TradingShadowState.Waiting,
    DateTimeOffset? EntryAt = null,
    DateTimeOffset? ClosedAt = null,
    decimal Quantity = 0,
    decimal EntryCostThb = 0,
    decimal? NetThb = null,
    decimal PeakBid = 0
);
