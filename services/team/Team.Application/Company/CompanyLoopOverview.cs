using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record CompanyLoopOverview(
    bool Enabled,
    CompanyLoopStatus Status,
    DateTimeOffset? NextRunAt,
    Guid? LastRunId,
    string? Error,
    decimal DailyNetProfitTargetThb = TradingMandate.DailyNetProfitTargetThb,
    string CalendarTimeZone = TradingMandate.CalendarTimeZone
);
