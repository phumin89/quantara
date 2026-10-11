using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveMeetingReportTests
{
    private static readonly DateTimeOffset Now = LiveRiskTests.Now;

    [Fact]
    public void Advisory_reasons_are_saved_exactly_and_legacy_reports_remain_readable()
    {
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []);
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        var report = Report() with { Status = "READ_ONLY", Summary = Report().Summary! with
            { AdvisoryOnly = true, AdvisoryReasons = ["EXECUTION_UNAVAILABLE"] } };
        tx.RecordMeeting(report, Now);
        Assert.Equal(new[] { "EXECUTION_UNAVAILABLE" }, Assert.Single(LiveMeetingReports.Read(tx.Account)).Summary!.AdvisoryReasons);
        Assert.Equal(account.Snapshot, tx.Account.Snapshot);
        Assert.Equal(account.Gate, tx.Account.Gate);
        LiveMeetingReports.Validate(Report(), LiveRiskTests.Account, Now);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("contradiction")]
    public void Advisory_reason_metadata_rejects_invented_or_contradictory_causes(string defect)
    {
        var reasons = defect switch { "unknown" => new[] { "UNKNOWN" }, "duplicate" => new[] { "EXECUTION_UNAVAILABLE", "EXECUTION_UNAVAILABLE" },
            "empty" => Array.Empty<string>(), _ => new[] { "EXECUTION_UNAVAILABLE" } };
        var report = Report() with { Summary = Report().Summary! with { AdvisoryOnly = defect != "contradiction", AdvisoryReasons = reasons } };
        Assert.Throws<InvalidDataException>(() => LiveMeetingReports.Validate(report, LiveRiskTests.Account, Now));
    }

    [Fact]
    public void RecordMeeting_persists_bounded_roles_without_mutating_account_money_or_gate()
    {
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []) { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        var before = account;
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        var report = Report() with { Summary = Report().Summary! with
        {
            Research = new(Now, new string('b', 64),
                [new("G-THB", 10, 11, 300000, -1, 10, 500, false, false)], [], [], []),
            Learning = new(Report().BusinessDate, Report().AccountContext!, CompanyLearningValidation.Roles.Select(role =>
                new CompanyRoleLearning(role, "NO_HISTORY", 0, null)).ToArray(), 0)
        } };
        tx.RecordMeeting(report, Now);

        var saved = Assert.Single(LiveMeetingReports.Read(tx.Account));
        Assert.Equal(Report().BusinessDate, saved.BusinessDate);
        Assert.Equal(Report().AttemptId, saved.AttemptId);
        Assert.Equal(Report().Status, saved.Status);
        Assert.Equal(Report().Summary!.Reports, saved.Summary!.Reports);
        Assert.Equal("G-THB", Assert.Single(saved.Summary.Research!.Watchlist).Symbol);
        Assert.Equal(8, saved.Summary.Learning!.Roles.Count);
        Assert.Equal(report.Summary!.Learning!.AccountContext, saved.Summary.Learning.AccountContext);
        Assert.Equal(before.Snapshot, tx.Account.Snapshot);
        Assert.Equal(before.Gate, tx.Account.Gate);
        Assert.Equal(before.Mandate, tx.Account.Mandate);
        Assert.Equal(before.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void RecordMeeting_is_idempotent_but_rejects_conflicting_identity_without_mutation()
    {
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []) { PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000) };
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        var report = Report();
        tx.RecordMeeting(report, Now);
        var afterFirst = tx.Account;
        tx.RecordMeeting(report, Now);
        Assert.Equal(afterFirst, tx.Account);

        var conflict = report with { Status = "HOLD" };
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(conflict, Now));
        Assert.Equal(afterFirst, tx.Account);
    }

    [Fact]
    public void RecordMeeting_rejects_mismatched_account_and_invalid_role_count()
    {
        var tx = new LiveAccountTransaction(new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate,
            LiveRiskTests.Gate, LiveRiskTests.Snapshot, [], [], []), (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(Report() with
        { AccountContext = new(TradingMode.Live, Guid.NewGuid().ToString("N")) }, Now));
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(Report() with
        { Summary = new(9, 0, 0, Report().Summary!.Reports) }, Now));
        Assert.Empty(LiveMeetingReports.Read(tx.Account));
    }

    [Fact]
    public void Quarter_hour_reports_coexist_and_remain_idempotent_without_changing_financial_state()
    {
        var account = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], []);
        var tx = new LiveAccountTransaction(account, (_, _) => Task.CompletedTask);
        var first = Report() with { ReviewHour = 9, ReviewMinute = 15 };
        var second = first with { AttemptId = Guid.NewGuid(), ReviewMinute = 30 };
        tx.RecordMeeting(first, Now.AddHours(1)); tx.RecordMeeting(second, Now.AddHours(1));
        tx.RecordMeeting(first, Now.AddHours(1));
        Assert.Equal(new[] { 30, 15 }, LiveMeetingReports.Read(tx.Account).Select(a => a.ReviewMinute));
        Assert.Equal(account.Gate, tx.Account.Gate); Assert.Equal(account.Mandate, tx.Account.Mandate);
        Assert.Equal(account.Snapshot, tx.Account.Snapshot); Assert.Empty(tx.Account.Orders);
        Assert.Throws<InvalidDataException>(() => tx.RecordMeeting(first with { AttemptId = Guid.NewGuid() }, Now.AddHours(1)));
    }

    [Fact]
    public void Report_minute_cannot_be_future_or_detached_from_a_review_hour()
    {
        Assert.Throws<InvalidDataException>(() => LiveMeetingReports.Validate(Report() with
            { ReviewHour = 9, ReviewMinute = 15 }, LiveRiskTests.Account, Now));
        Assert.Throws<InvalidDataException>(() => LiveMeetingReports.Validate(Report() with
            { ReviewMinute = 15 }, LiveRiskTests.Account, Now.AddHours(1)));
        Assert.Throws<InvalidDataException>(() => LiveMeetingReports.Validate(Report() with
            { ReviewHour = 9, ReviewMinute = 10 }, LiveRiskTests.Account, Now.AddHours(1)));
    }

    private static DailyMeetingAttempt Report() => new(new(2026, 9, 27),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "ORDER_PENDING", new(8, 80, 40,
        Enumerable.Range(0, 8).Select(i => new SavedAiReport($"role-{i}", "summary", "test-model", 10, 5,
            Guid.Parse($"{i + 1:D8}-0000-0000-0000-000000000000").ToString(), new string('a', 64), new(2026, 9, 26))).ToArray()))
        { AccountContext = new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")) };
}
