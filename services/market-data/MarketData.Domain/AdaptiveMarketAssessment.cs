namespace Quantara.MarketData.Domain;

public sealed record AdaptiveMarketAssessment(
    bool Available,
    string RuleVersion,
    MarketRegime Regime,
    MarketVolatility Volatility,
    int UsedBars,
    long? WindowFrom,
    long? CandleAt,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Atr14,
    decimal? AtrPercent,
    decimal? RelativeAtr,
    decimal? TrendGapAtr,
    decimal? Ema20SlopeAtr,
    string? UnavailableReason
);
