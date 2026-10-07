using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveMeetingIssueTests
{
    [Fact]
    public void Issues_accept_only_closed_codes_unique_entries_and_required_symbols()
    {
        Assert.True(LiveMeetingIssues.IsValid([new("MARKET_HISTORY_UNAVAILABLE", "BTC-THB"), new("MEETING_FAILED")]));
        Assert.False(LiveMeetingIssues.IsValid([new("UNKNOWN_CODE")]));
        Assert.False(LiveMeetingIssues.IsValid([new("MEETING_FAILED", "BTC-THB")]));
        Assert.False(LiveMeetingIssues.IsValid([new("MARKET_HISTORY_UNAVAILABLE")]));
        Assert.False(LiveMeetingIssues.IsValid([new("MEETING_FAILED"), new("MEETING_FAILED")]));
        Assert.False(LiveMeetingIssues.IsValid(Enumerable.Repeat(new LiveMeetingIssue("MEETING_FAILED"), 6).ToArray()));
        Assert.True(LiveMeetingIssues.IsValid(null));
    }

    [Fact]
    public void Activity_retry_window_is_inclusive_and_issue_validation_is_reused()
    {
        var observed = new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);
        var activity = new LiveCompanyActivity(
            new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")), Guid.NewGuid(), observed,
            [new("ceo", "working", observed)]) { NextRetryAt = observed.AddDays(1), Issues = [new("MEETING_FAILED")] };
        Assert.True(LiveActivityValidation.IsValid(activity, LiveRiskTests.Account, observed));
        Assert.True(LiveActivityValidation.IsValid(activity with { NextRetryAt = observed.AddDays(-1) }, LiveRiskTests.Account, observed));
        Assert.False(LiveActivityValidation.IsValid(activity with { NextRetryAt = observed.AddDays(1).AddTicks(1) }, LiveRiskTests.Account, observed));
        Assert.False(LiveActivityValidation.IsValid(activity with { Issues = [new("UNKNOWN_CODE")] }, LiveRiskTests.Account, observed));
    }

    [Fact]
    public void LiveMeetingReports_reject_bad_issue_metadata()
    {
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate, LiveRiskTests.Snapshot, [], [], []);
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(Report([new("MARKET_HISTORY_UNAVAILABLE")]), LiveRiskTests.Now));
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(Report([new("UNKNOWN_CODE")]), LiveRiskTests.Now));
    }

    [Fact]
    public void Legacy_journal_json_without_new_fields_still_loads_and_defaults_are_omitted()
    {
        var path = Path.Combine(Path.GetTempPath(), "quantara-legacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var attempt = new DailyMeetingAttempt(new(2026, 9, 12), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "COMPLETED");
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            // Independently construct the old wire shape, including a summary: the new
            // fields must not participate in its checksum when absent/default.
            var legacy = new[] { new { attempt.BusinessDate, attempt.AttemptId, attempt.Status,
                Summary = new { ModelCalls = 0, InputTokens = 0, OutputTokens = 0, Reports = Array.Empty<SavedAiReport>() } } };
            var attemptsJson = JsonSerializer.SerializeToUtf8Bytes(legacy, options);
            var hash = Convert.ToHexStringLower(SHA256.HashData(attemptsJson));
            File.WriteAllText(path, JsonSerializer.Serialize(new { hash, attempts = legacy }, options));
            var loaded = new JsonDailyMeetingJournal(path).Read(new(2026, 9, 12));
            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.Summary);
            Assert.Null(loaded.Summary.Issues);
            Assert.False(loaded.Summary.AdvisoryOnly);
            Assert.DoesNotContain("issues", JsonSerializer.Serialize(loaded, options), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("advisoryOnly", JsonSerializer.Serialize(loaded, options), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("issues", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("advisoryOnly", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static DailyMeetingAttempt Report(IReadOnlyList<LiveMeetingIssue> issues) => new(
        new(2026, 9, 27), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "ORDER_PENDING",
        new(0, 0, 0, []) { Issues = issues }) { AccountContext = new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")) };
}
