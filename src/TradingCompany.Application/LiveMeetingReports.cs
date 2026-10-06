using System.Text.Json;
using TradingCompany.Domain;

namespace TradingCompany.Application;

public static class LiveMeetingReports
{
    public const string AuditKind = "LiveMeetingCompleted";
    public static void Validate(DailyMeetingAttempt attempt, LiveAccountId account, DateTimeOffset now)
    {
        if (attempt.AccountContext != new TradingAccountContext(TradingMode.Live, account.Value.ToString("N")) ||
            attempt.AttemptId == Guid.Empty || attempt.BusinessDate == default ||
            attempt.BusinessDate > DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime) ||
            string.IsNullOrWhiteSpace(attempt.Status) || attempt.Status.Length > 100 || attempt.Status == "STARTED" ||
            attempt.Status.Any(c => !(char.IsAsciiLetterUpper(c) || c == '_')) ||
            attempt.ReviewHour is < 0 or > 23 ||
            (attempt.ReviewHour is { } hour && new DateTimeOffset(attempt.BusinessDate.ToDateTime(new TimeOnly(hour, 0)),
                TimeSpan.FromHours(7)) > now) ||
            (attempt.ReviewSnapshot is { } checkpoint && (checkpoint.AccountId != account || !checkpoint.Reconciled ||
                checkpoint.ObservedAt > now || DateOnly.FromDateTime(checkpoint.ObservedAt.ToOffset(TimeSpan.FromHours(7)).DateTime) != attempt.BusinessDate)) ||
            JsonSerializer.SerializeToUtf8Bytes(attempt).Length > 60 * 1024)
            throw new InvalidDataException("Invalid meeting report.");
        if (attempt.Summary is not { } summary) return;
        if (summary.ModelCalls is < 0 or > 8 || summary.InputTokens < 0 || summary.OutputTokens < 0 ||
            summary.Reports is null || summary.Reports.Count > 8 || summary.Reports.Any(r => r is null) ||
            summary.Reports.Select(r => r.Role).Distinct().Count() != summary.Reports.Count)
            throw new InvalidDataException("Invalid meeting report.");
        foreach (var r in summary.Reports)
            if (r is null || string.IsNullOrWhiteSpace(r.Role) || r.Role.Length > 100 ||
                r.Summary is null || r.Summary.Length > 6000 || string.IsNullOrWhiteSpace(r.ProviderModel) || r.ProviderModel.Length > 200 ||
                r.InputTokens < 0 || r.OutputTokens < 0 || !Guid.TryParse(r.ArtifactId, out _) ||
                r.EvidenceHash is not { Length: 64 } || r.EvidenceHash.Any(c => !char.IsAsciiHexDigit(c)) ||
                r.DataCutoff == default || r.DataCutoff > attempt.BusinessDate)
                throw new InvalidDataException("Invalid meeting report.");
    }
    public static IReadOnlyList<DailyMeetingAttempt> Read(LiveAccountState account) => account.Audit
        .Where(a => a.Kind == AuditKind).Select(a => JsonSerializer.Deserialize<DailyMeetingAttempt>(a.Detail)
            ?? throw new InvalidDataException("Invalid saved meeting.")).OrderByDescending(a => a.BusinessDate)
            .ThenByDescending(a => a.ReviewHour ?? 9).ToArray();
}

public sealed partial class LiveAccountTransaction
{
    public void RecordMeeting(DailyMeetingAttempt attempt, DateTimeOffset now)
    {
        LiveMeetingReports.Validate(attempt, Account.AccountId, now);
        var detail = JsonSerializer.Serialize(attempt);
        var existing = LiveMeetingReports.Read(Account).SingleOrDefault(a =>
            (a.BusinessDate == attempt.BusinessDate && a.ReviewHour == attempt.ReviewHour) || a.AttemptId == attempt.AttemptId);
        if (existing is not null)
        {
            if (JsonSerializer.Serialize(existing) != detail) throw new InvalidDataException("Meeting identity conflict.");
            return;
        }
        Audit(now, LiveMeetingReports.AuditKind, attempt.AttemptId, detail);
    }
}
