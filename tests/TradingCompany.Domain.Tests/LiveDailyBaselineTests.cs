using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveDailyBaselineTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 9, 28, 0, 0, 15, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset Previous = Midnight.AddSeconds(-30);

    [Fact]
    public void Automatic_rollover_is_disabled_by_default_and_preserves_the_existing_baseline()
    {
        var account = Account(new(2026, 9, 27), 1000, 1200, automatic: false);
        var tx = Transaction(account);
        var result = tx.Reconcile(Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.False(result.Complete);
        Assert.Contains("DAILY_BASELINE_REQUIRED", result.Violations);
        Assert.Equal(account.PerformanceBaseline, tx.Account.PerformanceBaseline);
    }

    [Fact]
    public void Automatic_rollover_requires_the_verified_previous_day_snapshot_and_resets_daily_state_only()
    {
        var account = Account(new(2026, 9, 27), 1000, 1200, automatic: true) with
        {
            Gate = LiveRiskTests.Gate,
            Snapshot = LiveRiskTests.Snapshot with { DailyTurnover = 75, DailyLoss = 20, ObservedAt = Previous }
        };
        var tx = Transaction(account);
        var result = tx.Reconcile(Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.True(result.Complete);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 28), 1000, 1200), tx.Account.PerformanceBaseline);
        Assert.Equal(0, tx.Account.Snapshot.DailyTurnover);
        Assert.Equal(0, tx.Account.Snapshot.DailyLoss);
        Assert.Equal(Midnight, tx.Account.Snapshot.ObservedAt);
        Assert.Empty(tx.Account.Orders);
        Assert.Empty(tx.Account.Fills);
        Assert.True(tx.Account.Gate.Armed);
        Assert.Equal(1m / 6m, tx.Account.Snapshot.Drawdown);
    }

    [Fact]
    public void Stable_observations_straddling_midnight_advance_the_day_without_losing_the_verified_anchor()
    {
        var account = Account(new(2026, 9, 27), 1000, 1200, automatic: true) with
        { Gate = LiveRiskTests.Gate, Snapshot = LiveRiskTests.Snapshot with { ObservedAt = Previous } };
        var tx = Transaction(account);
        var result = tx.Reconcile(Evidence(Midnight.AddSeconds(-20)), Evidence(Midnight), Midnight);
        Assert.True(result.Complete);
        Assert.True(tx.Account.Gate.Armed);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 28), 1000, 1200), tx.Account.PerformanceBaseline);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("clockback")]
    [InlineData("changed")]
    [InlineData("deposit")]
    [InlineData("incomplete")]
    [InlineData("credentials")]
    [InlineData("wrong-account")]
    public void Automatic_rollover_fails_closed_for_untrusted_evidence(string defect)
    {
        var account = Account(new(2026, 9, 27), 1000, 1200, automatic: true);
        var first = Evidence(Midnight);
        var second = Evidence(Midnight);
        switch (defect)
        {
            case "future": first = second = Evidence(Midnight.AddSeconds(1)); break;
            case "clockback": first = second = Evidence(Previous.AddDays(-1)); break;
            case "changed": second = second with { CashBalance = 1001 }; break;
            case "deposit": first = second = Evidence(Previous) with { CashBalance = 2000 }; break;
            case "incomplete": first = first with { Complete = false }; break;
            case "credentials": second = second with { CredentialFingerprint = "other-key" }; break;
            case "wrong-account": first = second = second with { AccountId = new(Guid.NewGuid()) }; break;
        }

        var tx = Transaction(account);
        var result = tx.Reconcile(first, second, Midnight);
        Assert.False(result.Complete);
        Assert.Equal(account.PerformanceBaseline, tx.Account.PerformanceBaseline);
        Assert.Equal(1000, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void Manual_rollover_accepts_a_fresh_previous_day_evidence_without_arming_or_resetting_high_water()
    {
        var account = Account(new(2026, 9, 20), 1000, 1200, automatic: false);
        var tx = Transaction(account);
        var result = tx.RollDailyBaseline(Request(tx), Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.True(result.Complete);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 28), 1000, 1200), tx.Account.PerformanceBaseline);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Equal(0, tx.Account.Snapshot.DailyTurnover);
        Assert.Equal(0, tx.Account.Snapshot.DailyLoss);
    }

    [Fact]
    public void Stopped_account_remains_stopped_when_manual_baseline_is_created()
    {
        var account = Account(new(2026, 9, 20), 1000, 1200, automatic: false) with
        { Gate = LiveRiskTests.Gate with { Stopped = true, Armed = false } };
        var tx = Transaction(account);
        var result = tx.RollDailyBaseline(Request(tx), Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.True(result.Complete);
        Assert.True(tx.Account.Gate.Stopped);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("mandate")]
    [InlineData("snapshot")]
    public void Manual_rollover_rejects_wrong_request_identity_or_versions(string defect)
    {
        var tx = Transaction(Account(new(2026, 9, 20), 1000, 1200, automatic: false));
        var request = Request(tx);
        request = defect switch
        {
            "account" => request with { AccountId = new(Guid.NewGuid()) },
            "mandate" => request with { MandateVersion = request.MandateVersion + 1 },
            _ => request with { SnapshotVersion = request.SnapshotVersion + 1 }
        };

        var result = tx.RollDailyBaseline(request, Evidence(Midnight), Evidence(Midnight), Midnight);
        Assert.False(result.Complete);
        Assert.Contains(defect == "account" ? "ACCOUNT_MISMATCH" : "STALE_APPROVAL", result.Violations);
        Assert.Equal(new DateOnly(2026, 9, 20), tx.Account.PerformanceBaseline!.BangkokDate);
        Assert.Equal(1000, tx.Account.Snapshot.CashBalance);
    }

    [Fact]
    public void Manual_rollover_cannot_reset_a_baseline_on_the_same_bangkok_day()
    {
        var tx = Transaction(Account(new(2026, 9, 28), 1000, 1200, automatic: false));
        var result = tx.RollDailyBaseline(Request(tx), Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.False(result.Complete);
        Assert.Contains("DAILY_BASELINE_REQUIRED", result.Violations);
        Assert.Equal(new DateOnly(2026, 9, 28), tx.Account.PerformanceBaseline!.BangkokDate);
    }

    [Fact]
    public void Old_or_unreconciled_evidence_may_reconcile_money_but_disarms_and_keeps_the_old_baseline()
    {
        var account = Account(new(2026, 9, 27), 1000, 1200, automatic: true) with
        { Snapshot = LiveRiskTests.Snapshot with { Reconciled = false, ObservedAt = Previous } };
        var tx = Transaction(account);
        var result = tx.Reconcile(Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.False(result.Complete);
        Assert.Contains("DAILY_BASELINE_REQUIRED", result.Violations);
        Assert.Equal(account.PerformanceBaseline, tx.Account.PerformanceBaseline);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    public void Missing_or_stale_daily_baseline_cannot_reserve_or_dispatch(string kind)
    {
        var baseline = kind == "missing" ? null : new LivePerformanceBaseline(new(2026, 9, 26), 1000, 1000);
        var account = Account(new(2026, 9, 27), 1000, 1000, automatic: false) with { PerformanceBaseline = baseline };
        var tx = Transaction(account);
        var order = tx.Reserve(LiveRiskTests.Intent, LiveRiskTests.Now);

        Assert.Equal(LiveOrderState.Rejected, order.State);
        Assert.Contains("DAILY_BASELINE_REQUIRED", order.Violations);
    }

    [Fact]
    public void Automatic_rollover_does_not_rearm_an_expired_mandate()
    {
        var account = Account(new(2026, 9, 27), 1000, 1000, automatic: true) with
        {
            Gate = LiveRiskTests.Gate,
            Mandate = LiveRiskTests.Mandate with { ExpiresAt = Midnight.AddSeconds(-1) },
            Snapshot = LiveRiskTests.Snapshot with { ObservedAt = Previous }
        };
        var tx = Transaction(account);
        var result = tx.Reconcile(Evidence(Midnight), Evidence(Midnight), Midnight);

        Assert.False(result.Complete);
        Assert.False(tx.Account.Gate.Armed);
    }

    private static LiveAccountState Account(DateOnly date, decimal dayStart, decimal highWater, bool automatic)
        => new(LiveRiskTests.Account, LiveRiskTests.Mandate with { AutomaticDailyRollover = automatic },
            LiveRiskTests.Gate with { Armed = false }, LiveRiskTests.Snapshot, [], [], [])
        { PerformanceBaseline = new(date, dayStart, highWater) };

    private static LiveBaselineRequest Request(LiveAccountTransaction tx)
        => new(tx.Account.AccountId, tx.Account.Mandate.Version, tx.Account.Snapshot.Version);

    private static LiveBrokerEvidence Evidence(DateTimeOffset observedAt)
        => new(LiveRiskTests.Account, "key-fingerprint", observedAt, true, 1000, 0, [], [], [],
            [new("BTC-THB", 99900, 100000, observedAt)]);

    private static LiveAccountTransaction Transaction(LiveAccountState account)
        => new(account, (_, _) => Task.CompletedTask);
}
