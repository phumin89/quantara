using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveInitialBaselineTests
{
    private static LiveAccountState Account => new(LiveRiskTests.Account, LiveRiskTests.Mandate,
        LiveRiskTests.Gate with { Armed = false }, LiveRiskTests.Snapshot with { Reconciled = false }, [], [], []);
    private static LiveBaselineRequest Request => new(LiveRiskTests.Account, 1, 1);
    private static LiveBrokerEvidence Evidence => new(LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now,
        true, 1000m, 0m, [], [], [], LiveRiskTests.Snapshot.Quotes);
    private static LiveAccountTransaction Transaction(LiveAccountState? account = null)
        => new(account ?? Account, (_, _) => Task.CompletedTask);

    [Fact]
    public void Stable_cash_only_account_establishes_initial_baseline_without_arming_or_changing_limits()
    {
        var account = Account;
        var tx = Transaction(account with { Gate = account.Gate with { Stopped = true } });
        var result = tx.InitializeBaseline(Request, Evidence, Evidence, LiveRiskTests.Now);
        Assert.True(result.Complete);
        Assert.Equal(new LivePerformanceBaseline(new(2026, 9, 27), 1000m, 1000m), tx.Account.PerformanceBaseline);
        Assert.True(tx.Account.Snapshot.Reconciled);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Equal(0, tx.Account.Gate.ArmedMandateVersion);
        Assert.True(tx.Account.Gate.Stopped);
        Assert.Equal(account.Mandate, tx.Account.Mandate);
        Assert.Null(tx.Account.FirstActivatedAt);
        Assert.Single(tx.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
        Assert.Single(tx.Account.Audit, a => a.Kind == "ReconciliationEvidence");
        var baseline = tx.Account.PerformanceBaseline;
        Assert.False(tx.InitializeBaseline(Request with { SnapshotVersion = tx.Account.Snapshot.Version },
            Evidence, Evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(baseline, tx.Account.PerformanceBaseline);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("snapshot")]
    [InlineData("mandate")]
    [InlineData("activated")]
    [InlineData("existing")]
    [InlineData("armed")]
    [InlineData("limits")]
    [InlineData("turnover")]
    [InlineData("reserved")]
    [InlineData("order")]
    [InlineData("changed")]
    [InlineData("deposit")]
    [InlineData("incomplete")]
    [InlineData("key")]
    [InlineData("stale")]
    [InlineData("position")]
    [InlineData("version-zero")]
    public void Invalid_onboarding_does_not_create_baseline_or_mutate_money(string defect)
    {
        var account = Account;
        var request = Request;
        var first = Evidence;
        var second = Evidence;
        switch (defect)
        {
            case "account": request = request with { AccountId = new(Guid.NewGuid()) }; break;
            case "snapshot": request = request with { SnapshotVersion = 2 }; break;
            case "mandate": request = request with { MandateVersion = 2 }; break;
            case "activated": account = account with { FirstActivatedAt = LiveRiskTests.Now }; break;
            case "existing": account = account with { PerformanceBaseline = new(new(2026, 9, 26), 1100m, 1200m) }; break;
            case "armed": account = account with { Gate = LiveRiskTests.Gate }; break;
            case "limits": account = account with { Mandate = account.Mandate with { CapitalAllocation = 0 } }; break;
            case "turnover": account = account with { Snapshot = account.Snapshot with { DailyTurnover = 1 } }; break;
            case "reserved": account = account with { Snapshot = account.Snapshot with { CashReserved = 1 } }; break;
            case "order": account = LiveReconciliationTests.Seed().Account with { PerformanceBaseline = null }; break;
            case "changed": second = second with { CashBalance = 1001m }; break;
            case "deposit": first = second = second with { CashBalance = 2000m }; break;
            case "incomplete": first = first with { Complete = false }; break;
            case "key": first = second = second with { CredentialFingerprint = "another-key" }; break;
            case "stale": first = second = second with { ObservedAt = LiveRiskTests.Now.AddSeconds(-31) }; break;
            case "position": first = second = second with { Positions = [new("BTC-THB", .001m, 0)] }; break;
            case "version-zero": account = account with { Snapshot = account.Snapshot with { Version = 0 } }; request = request with { SnapshotVersion = 0 }; break;
        }
        var tx = Transaction(account);
        Assert.False(tx.InitializeBaseline(request, first, second, LiveRiskTests.Now).Complete);
        Assert.Equal(account.PerformanceBaseline, tx.Account.PerformanceBaseline);
        Assert.Equal(account.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Equal(account.Snapshot.CashReserved, tx.Account.Snapshot.CashReserved);
        Assert.Equal(account.Snapshot.Positions, tx.Account.Snapshot.Positions);
        Assert.False(tx.Account.Gate.Armed);
        Assert.DoesNotContain(tx.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
    }

    [Fact]
    public void Initial_date_is_bangkok_date_and_initialization_cannot_roll_forward_an_existing_baseline()
    {
        var now = new DateTimeOffset(2026, 9, 27, 17, 0, 0, TimeSpan.Zero);
        var evidence = Evidence with { ObservedAt = now, Quotes = [] };
        var tx = Transaction();
        Assert.True(tx.InitializeBaseline(Request, evidence, evidence, now).Complete);
        Assert.Equal(new DateOnly(2026, 9, 28), tx.Account.PerformanceBaseline!.BangkokDate);
        var baseline = tx.Account.PerformanceBaseline;
        var tomorrow = now.AddDays(1);
        evidence = evidence with { ObservedAt = tomorrow };
        Assert.False(tx.InitializeBaseline(Request with { SnapshotVersion = tx.Account.Snapshot.Version }, evidence, evidence, tomorrow).Complete);
        Assert.Equal(baseline, tx.Account.PerformanceBaseline);
    }
}
