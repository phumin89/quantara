namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingDailyReport(
    string Date,
    TradingMode Mode,
    int Revision,
    DateTimeOffset CreatedAt,
    string Text,
    string Status,
    decimal? StartingEquityThb,
    decimal? EndingEquityThb,
    decimal? RealizedNetThb,
    string Fingerprint,
    DateTimeOffset? ClaimedAt = null,
    long? MessageId = null,
    DateTimeOffset? ClosedAt = null,
    string? EvidenceHash = null,
    string? StartingEvidenceHash = null,
    TradingMode? OperatingMode = null
);
