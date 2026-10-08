using TradingCompany.Domain;

namespace TradingCompany.Application;

public sealed record LiveMeetingIssue(string Code, string? Symbol = null);

public static class LiveMeetingIssues
{
    public static bool IsValid(IReadOnlyList<LiveMeetingIssue>? issues) => issues is null ||
        issues.Count <= 5 && issues.All(i => i is not null && (i.Code == "MARKET_HISTORY_UNAVAILABLE"
            ? i.Symbol is "BTC-THB" or "ETH-THB" or "SOL-THB"
            : i.Symbol is null && i.Code is "PUBLIC_RESEARCH_UNAVAILABLE" or "LIVE_READ_UNAVAILABLE" or
                "ACCOUNT_STATE_INVALID" or "JOURNAL_UNAVAILABLE" or "MEETING_FAILED")) &&
        issues.Distinct().Count() == issues.Count;
}

public sealed record SavedAiReport(
    string Role,
    string Summary,
    string ProviderModel,
    int InputTokens,
    int OutputTokens,
    string ArtifactId,
    string EvidenceHash,
    DateOnly DataCutoff);

public sealed record DailyMeetingSummary(
    int ModelCalls,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<SavedAiReport> Reports)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CoinResearchBrief? Research { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LiveMeetingIssue>? Issues { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AdvisoryOnly { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CompanyLearningReport? Learning { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TradePlanPolicy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PlanRejectionReasons { get; init; }
}

public sealed record DailyMeetingAttempt(
    DateOnly BusinessDate,
    Guid AttemptId,
    string Status,
    DailyMeetingSummary? Summary = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? ReviewHour = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    LiveSnapshot? ReviewSnapshot = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TradingAccountContext? AccountContext { get; init; }
}

public interface IDailyMeetingJournal
{
    TradingAccountContext? AccountContext => null;
    void EnsureReconciled();
    DailyMeetingAttempt? Read(DateOnly businessDate);
    DailyMeetingAttempt? Read(DateOnly businessDate, int reviewHour) => throw new NotSupportedException();
    IReadOnlyList<DailyMeetingAttempt> ReadAll();
    bool TryBegin(DateOnly businessDate, Guid attemptId);
    bool TryBegin(DateOnly businessDate, Guid attemptId, int? reviewHour, LiveSnapshot? reviewSnapshot) => throw new NotSupportedException();
    void Complete(DateOnly businessDate, Guid attemptId, string status, DailyMeetingSummary? summary = null);
}
