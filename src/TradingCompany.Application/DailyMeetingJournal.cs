using TradingCompany.Domain;

namespace TradingCompany.Application;

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
    IReadOnlyList<SavedAiReport> Reports);

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
