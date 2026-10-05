using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveReconciliationTests
{
    [Fact]
    public void Cancel_race_releases_only_after_terminal_order_and_all_fills_match_wallet()
    {
        var tx = Seed(.003m);
        var evidence = Evidence();
        evidence = evidence with
        {
            CashReserved = 200m,
            Orders = [evidence.Orders[0] with { Quantity = .003m, State = LiveOrderState.PartiallyFilled, ReservedCash = 200m }]
        };
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(200.65m, tx.Account.Snapshot.CashReserved);
        tx.RequestCancellation(LiveRiskTests.Intent.Id, LiveRiskTests.Now);
        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(200.65m, tx.Account.Snapshot.CashReserved);
        evidence = evidence with
        {
            CashBalance = 799.5m, CashReserved = 0,
            Positions = [new("BTC-THB", .002m, 0)],
            Orders = [evidence.Orders[0] with { State = LiveOrderState.Cancelled, FilledQuantity = .002m, ReservedCash = 0 }],
            Fills = [.. evidence.Fills, evidence.Fills[0] with { TransactionId = "racing-fill" }]
        };
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(799.5m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(.002m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        Assert.Equal(0, tx.Account.Snapshot.CashReserved);
        Assert.Equal(LiveOrderState.Cancelled, Assert.Single(tx.Account.Orders).State);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Fact]
    public void Stable_owned_fill_reconciles_once_and_updates_marked_risk_without_arming()
    {
        var tx = Seed();
        var evidence = Evidence();
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(899.75m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(.001m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        Assert.Equal(999.65m, tx.Account.Snapshot.Equity);
        Assert.Equal(.35m, tx.Account.Snapshot.DailyLoss);
        Assert.Equal(.00035m, tx.Account.Snapshot.Drawdown);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Equal(LiveOrderState.Filled, Assert.Single(tx.Account.Orders).State);
        Assert.Equal(0, tx.Account.Snapshot.CashReserved);
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(899.75m, tx.Account.Snapshot.CashBalance);
        Assert.Single(tx.Account.Fills);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("cash")]
    [InlineData("coins")]
    [InlineData("missing-order")]
    [InlineData("credential")]
    [InlineData("stale")]
    public void Inconsistent_evidence_cannot_commit_money_or_release_reservations(string defect)
    {
        var tx = Seed();
        var evidence = Evidence();
        evidence = defect switch
        {
            "incomplete" => evidence with { Complete = false },
            "cash" => evidence with { CashBalance = 900m },
            "coins" => evidence with { Positions = [new("BTC-THB", .002m, 0)] },
            "missing-order" => evidence with { Orders = [] },
            "credential" => evidence with { CredentialFingerprint = "different-key" },
            _ => evidence with { ObservedAt = LiveRiskTests.Now.AddSeconds(-31) }
        };
        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(100.3m, tx.Account.Snapshot.CashReserved);
        Assert.Empty(tx.Account.Fills);
        Assert.False(tx.Account.Snapshot.Reconciled);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Fact]
    public void Changing_broker_observations_do_not_become_a_consistent_snapshot()
    {
        var tx = Seed();
        Assert.False(tx.Reconcile(Evidence(), Evidence() with { CashBalance = 901m }, LiveRiskTests.Now).Complete);
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Fills);
    }

    [Fact]
    public void Unknown_dispatch_is_linked_only_by_exact_unique_client_reference()
    {
        var original = Seed().Account;
        var unknown = original with { Orders = [original.Orders[0] with { State = LiveOrderState.Unknown, BrokerId = null }] };
        var tx = new LiveAccountTransaction(unknown, (_, _) => Task.CompletedTask);
        var evidence = Evidence();
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal("owned-order", Assert.Single(tx.Account.Orders).BrokerId);
        var wrong = new LiveAccountTransaction(unknown, (_, _) => Task.CompletedTask);
        evidence = evidence with { Orders = [evidence.Orders[0] with { ClientId = "somebody-else" }] };
        Assert.False(wrong.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Null(Assert.Single(wrong.Account.Orders).BrokerId);
        Assert.Equal(100.3m, wrong.Account.Snapshot.CashReserved);
    }

    [Fact]
    public void Fee_correction_changes_cash_once_and_preserves_original_audit()
    {
        var tx = Seed();
        var evidence = Evidence();
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        evidence = evidence with { CashBalance = 899.8m, Fills = [evidence.Fills[0] with { CashFee = .2m, Revision = 2 }] };
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);
        Assert.Equal(899.8m, tx.Account.Snapshot.CashBalance);
        Assert.Equal(.001m, Assert.Single(tx.Account.Snapshot.Positions).Quantity);
        Assert.Single(tx.Account.Audit, x => x.Kind == "FeeCorrected");
    }

    [Fact]
    public void Missing_daily_risk_baseline_cannot_enable_trading()
    {
        var tx = new LiveAccountTransaction(Seed().Account with { PerformanceBaseline = null }, (_, _) => Task.CompletedTask);
        var result = tx.Reconcile(Evidence(), Evidence(), LiveRiskTests.Now);
        Assert.False(result.Complete);
        Assert.Contains("DAILY_BASELINE_REQUIRED", result.Violations);
        Assert.False(tx.Account.Gate.Armed);
    }

    internal static LiveAccountTransaction Seed(decimal quantity = .001m)
    {
        var tx = new LiveAccountTransaction(new(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate,
            LiveRiskTests.Snapshot, [], [], [])
        {
            PerformanceBaseline = new(new(2026, 9, 27), 1000m, 1000m)
        }, (_, _) => Task.CompletedTask);
        var intent = LiveRiskTests.Intent with { Quantity = quantity };
        tx.Reserve(intent, LiveRiskTests.Now);
        tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "owned-order", LiveRiskTests.Now);
        tx.SetStopped(true);
        return tx;
    }

    internal static LiveBrokerEvidence Evidence() => new(LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now, true,
        899.75m, 0m, [new("BTC-THB", .001m, 0)],
        [new(LiveRiskTests.Intent.Id.ToString("N"), "owned-order", "BTC-THB", OrderSide.Buy, .001m, 100000m, LiveOrderState.Filled, .001m, 0m, 0m)],
        [new(LiveRiskTests.Account, "fill-1", "owned-order", OrderSide.Buy, "BTC-THB", .001m, 100000m, .25m, 1)],
        LiveRiskTests.Snapshot.Quotes);
}
