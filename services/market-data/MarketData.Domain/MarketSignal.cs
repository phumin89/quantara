namespace Quantara.MarketData.Domain;

public sealed record MarketSignal(
    string Id,
    string Symbol,
    SignalDirection Direction,
    long CandleAt,
    DateTimeOffset DetectedAt,
    decimal Price,
    decimal PriorLevel,
    decimal VolumeRatio,
    decimal SpreadPercent,
    string RuleVersion
);
