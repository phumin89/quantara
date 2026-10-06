using TradingCompany.Domain;

namespace TradingCompany.Application;

// Advisory process receives no broker credential, activation authority or storage capability.
public sealed record LiveOrderSizing(string Symbol, decimal PriceStep, decimal BaseStep, decimal QuoteStep, decimal MinimumQuoteSize);
public sealed record LiveResearchLimits(decimal CapitalAllocation, decimal MaximumPositionWeight,
    int MaximumOpenPositions, decimal FeeReserveRate, decimal MaximumSlippage, IReadOnlyList<string> AllowedSymbols)
{
    public IReadOnlyList<LiveOrderSizing> OrderSizing { get; init; } = [];
}
public sealed record LiveResearchPacket(TradingAccountContext AccountContext, long MandateVersion,
    LiveSnapshot Snapshot, LiveResearchLimits Limits, bool Armed, bool Stopped);
public sealed record LiveIntentReceipt(LiveIntent Intent, LiveOrderState State, IReadOnlyList<string> Violations);
// Display-only telemetry. It carries no proposal, authority, prompt or broker credential.
public sealed record LiveRoleActivity(string Role, string Status, DateTimeOffset ChangedAt);
public sealed record LiveCompanyActivity(TradingAccountContext AccountContext, Guid? MeetingId,
    DateTimeOffset ObservedAt, IReadOnlyList<LiveRoleActivity> Roles);
public static class LiveActivityValidation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(45);
    public static bool IsValid(LiveCompanyActivity activity, LiveAccountId account, DateTimeOffset now) =>
        account.Value != Guid.Empty && activity.AccountContext == new TradingAccountContext(TradingMode.Live, account.Value.ToString("N")) &&
        activity.ObservedAt <= now.AddSeconds(5) && now - activity.ObservedAt < Lifetime &&
        activity.Roles is { Count: <= 8 } && (activity.MeetingId is { } id ? id != Guid.Empty : activity.Roles.Count == 0) &&
        activity.Roles.All(r => r is not null && IsRole(r.Role) && IsStatus(r.Status) && r.ChangedAt <= activity.ObservedAt) &&
        activity.Roles.Select(r => r.Role).Distinct(StringComparer.Ordinal).Count() == activity.Roles.Count &&
        activity.Roles.Count(r => r.Status == "working") <= 1;

    public static bool IsRole(string role) => role is "ceo" or "chief-of-staff" or "market-research" or "security-analysis" or
        "quant-research" or "portfolio-initial" or "risk-critic" or "portfolio-final";
    public static bool IsStatus(string status) => status is "idle" or "working" or "completed" or "failed";
}
public interface ILiveCompanyClient
{
    Task<LiveResearchPacket> ReadAsync(LiveAccountId accountId, CancellationToken cancellationToken);
    Task<LiveIntentReceipt> SubmitAsync(LiveIntent intent, CancellationToken cancellationToken);
    Task SaveMeetingAsync(DailyMeetingAttempt attempt, CancellationToken cancellationToken);
    Task SaveActivityAsync(LiveCompanyActivity activity, CancellationToken cancellationToken) => Task.CompletedTask;
}
