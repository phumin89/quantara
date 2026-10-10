using TradingCompany.Domain;

namespace TradingCompany.Application;

// Advisory process receives no broker credential, activation authority or storage capability.
public sealed record LiveOrderSizing(string Symbol, decimal PriceStep, decimal BaseStep, decimal QuoteStep, decimal MinimumQuoteSize);
public sealed record LiveResearchLimits(decimal CapitalAllocation, decimal MaximumPositionWeight,
    int MaximumOpenPositions, decimal FeeReserveRate, decimal MaximumSlippage, IReadOnlyList<string> AllowedSymbols)
{
    public IReadOnlyList<LiveOrderSizing> OrderSizing { get; init; } = [];
    public decimal? MaximumOrderNotional { get; init; }
    public decimal? MaximumDailyTurnover { get; init; }
    public bool UseFullPortfolio { get; init; }
    public IReadOnlyList<string> ResearchSymbols { get; init; } = [];
}
public sealed record LiveResearchPacket(TradingAccountContext AccountContext, long MandateVersion,
    LiveSnapshot Snapshot, LiveResearchLimits Limits, bool Armed, bool Stopped)
{
    // Old executors omit this field and accept hourly report identities only.
    public int MinimumReviewIntervalMinutes { get; init; } = 60;
    // Public quotes for comparison do not enlarge the owner's execution mandate.
    public IReadOnlyList<LiveQuote> ResearchQuotes { get; init; } = [];
    public IReadOnlyList<LiveTradePlanView> TradePlans { get; init; } = [];
}
public sealed record LiveIntentReceipt(LiveIntent Intent, LiveOrderState State, IReadOnlyList<string> Violations);
// Display-only telemetry. It carries no proposal, authority, prompt or broker credential.
public sealed record LiveRoleActivity(string Role, string Status, DateTimeOffset ChangedAt);
public sealed record LiveCompanyActivity(TradingAccountContext AccountContext, Guid? MeetingId,
    DateTimeOffset ObservedAt, IReadOnlyList<LiveRoleActivity> Roles)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CoordinatorStatus { get; init; }
    public IReadOnlyList<string> ResearchSymbols { get; init; } = [];
    public IReadOnlyList<string> TradingSymbols { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LiveMeetingIssue>? Issues { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRetryAt { get; init; }
}
public static class LiveActivityValidation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(45);
    public static bool IsValid(LiveCompanyActivity activity, LiveAccountId account, DateTimeOffset now) =>
        account.Value != Guid.Empty && activity.AccountContext == new TradingAccountContext(TradingMode.Live, account.Value.ToString("N")) &&
        activity.ObservedAt <= now.AddSeconds(5) && now - activity.ObservedAt < Lifetime &&
        activity.Roles is { Count: <= 8 } && (activity.MeetingId is { } id ? id != Guid.Empty : activity.Roles.Count == 0) &&
        activity.Roles.All(r => r is not null && IsRole(r.Role) && IsStatus(r.Status) && r.ChangedAt <= activity.ObservedAt) &&
        activity.Roles.Select(r => r.Role).Distinct(StringComparer.Ordinal).Count() == activity.Roles.Count &&
        activity.Roles.Count(r => r.Status == "working") <= 1 &&
        (activity.CoordinatorStatus is null || activity.CoordinatorStatus.Length is > 0 and <= 100 &&
            activity.CoordinatorStatus.All(c => char.IsAsciiLetterUpper(c) || c == '_')) &&
        ValidSymbols(activity.ResearchSymbols) && ValidSymbols(activity.TradingSymbols) &&
        activity.TradingSymbols.All(activity.ResearchSymbols.Contains) && LiveMeetingIssues.IsValid(activity.Issues) &&
        (activity.NextRetryAt is null || activity.NextRetryAt >= activity.ObservedAt.AddDays(-1) &&
            activity.NextRetryAt <= activity.ObservedAt.AddDays(1));

    private static bool ValidSymbols(IReadOnlyList<string>? symbols) => symbols is { Count: <= LiveMarketSymbol.MaximumPayloadMarkets } &&
        symbols.All(LiveMarketSymbol.IsValid) && symbols.Distinct(StringComparer.Ordinal).Count() == symbols.Count;

    public static bool IsRole(string role) => role is "ceo" or "chief-of-staff" or "market-research" or "security-analysis" or
        "quant-research" or "portfolio-initial" or "risk-critic" or "portfolio-final";
    public static bool IsStatus(string status) => status is "idle" or "working" or "completed" or "failed";
}
public interface ILiveCompanyClient
{
    Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken);
    Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken);
    Task<LiveTradePlanReceipt> RegisterPlanAsync(LiveTradePlanRequest plan, CancellationToken cancellationToken)
        => throw new NotSupportedException("Trade plan registration is unavailable.");
    Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken);
    Task SaveActivityAsync(LiveCompanyActivity activity, CancellationToken cancellationToken) => Task.CompletedTask;
}
