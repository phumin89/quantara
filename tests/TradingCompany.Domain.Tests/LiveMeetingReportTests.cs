using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveMeetingReportTests
{
    private static readonly DateTimeOffset Now = LiveRiskTests.Now;

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
                [new("G-THB", 10, 11, 300000, -1, 10, 500, false, false)], [], [], [])
        } };
        tx.RecordMeeting(report, Now);

        var saved = Assert.Single(LiveMeetingReports.Read(tx.Account));
        Assert.Equal(Report().BusinessDate, saved.BusinessDate);
        Assert.Equal(Report().AttemptId, saved.AttemptId);
        Assert.Equal(Report().Status, saved.Status);
        Assert.Equal(Report().Summary!.Reports, saved.Summary!.Reports);
        Assert.Equal("G-THB", Assert.Single(saved.Summary.Research!.Watchlist).Symbol);
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

    private static DailyMeetingAttempt Report() => new(new(2026, 9, 27),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "ORDER_PENDING", new(8, 80, 40,
        Enumerable.Range(0, 8).Select(i => new SavedAiReport($"role-{i}", "summary", "test-model", 10, 5,
            Guid.Parse($"{i + 1:D8}-0000-0000-0000-000000000000").ToString(), new string('a', 64), new(2026, 9, 26))).ToArray()))
        { AccountContext = new(TradingMode.Live, LiveRiskTests.Account.Value.ToString("N")) };
}
