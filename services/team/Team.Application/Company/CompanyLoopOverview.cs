using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record CompanyLoopOverview(
    bool Enabled,
    CompanyLoopStatus Status,
    DateTimeOffset? NextRunAt,
    Guid? LastRunId,
    string? Error,
    decimal DailyNetProfitTargetThb = TradingMandate.DailyNetProfitTargetThb,
    string CalendarTimeZone = TradingMandate.CalendarTimeZone,
    PortfolioGoal? CurrentGoal = null,
    DateTimeOffset? LastCompletedAt = null,
    TeamRole? ActiveRole = null,
    TeamRole? NextRole = null,
    string? BudgetReason = null,
    string BudgetMode = "boundedRequests"
);
