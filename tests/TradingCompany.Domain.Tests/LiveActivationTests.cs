using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveActivationTests
{
    [Fact]
    public void Owner_must_confirm_the_exact_account_mandate_and_snapshot()
    {
        var tx = Transaction();
        var result = tx.Arm(Request, Preflight, LiveRiskTests.Now);
        Assert.True(result.Allowed);
        Assert.True(tx.Account.Gate.Armed);
        Assert.Equal(1, tx.Account.Gate.ArmedMandateVersion);
        Assert.Equal(LiveRiskTests.Now, tx.Account.FirstActivatedAt);
        Assert.Contains(tx.Account.Audit, e => e.Kind == "OwnerArmed");
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Orders);
    }

    [Theory]
    [InlineData("confirmation")]
    [InlineData("account")]
    [InlineData("broker")]
    [InlineData("mandate")]
    [InlineData("snapshot")]
    public void Stale_or_wrong_owner_confirmation_cannot_arm(string failure)
    {
        var request = failure switch
        {
            "confirmation" => Request with { ConfirmLive = false },
            "account" => Request with { AccountId = new(Guid.Empty) },
            "broker" => Request with { BrokerAccountReference = "other-broker-account" },
            "mandate" => Request with { MandateVersion = 2 },
            _ => Request with { SnapshotVersion = 2 }
        };
        var tx = Transaction();
        Assert.False(tx.Arm(request, Preflight, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("isolation")]
    [InlineData("permissions")]
    [InlineData("inventory")]
    [InlineData("fingerprint")]
    [InlineData("snapshot")]
    [InlineData("stale")]
    [InlineData("future")]
    public void Missing_or_mismatched_executor_preflight_blocks_arming(string failure)
    {
        var evidence = failure switch
        {
            "credentials" => Preflight with { CredentialsVerified = false },
            "isolation" => Preflight with { IsolationVerified = false },
            "permissions" => Preflight with { PermissionsVerified = false },
            "inventory" => Preflight with { InitialInventoryEmpty = false },
            "fingerprint" => Preflight with { CredentialFingerprint = "replacement-key" },
            "snapshot" => Preflight with { SnapshotVersion = 2 },
            "stale" => Preflight with { ObservedAt = LiveRiskTests.Now.AddSeconds(-31) },
            _ => Preflight with { ObservedAt = LiveRiskTests.Now.AddSeconds(1) }
        };
        var tx = Transaction();
        Assert.False(tx.Arm(Request, evidence, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Theory]
    [InlineData("zero-limit")]
    [InlineData("unreconciled")]
    [InlineData("expired")]
    [InlineData("baseline")]
    [InlineData("stale-snapshot")]
    [InlineData("initial-holdings")]
    [InlineData("initial-reserve")]
    [InlineData("over-reserved")]
    public void Unsafe_saved_account_cannot_arm(string failure)
    {
        var account = Seed();
        account = failure switch
        {
            "zero-limit" => account with { Mandate = account.Mandate with { CapitalAllocation = 0 } },
            "unreconciled" => account with { Snapshot = account.Snapshot with { Reconciled = false } },
            "expired" => account with { Mandate = account.Mandate with { ExpiresAt = LiveRiskTests.Now } },
            "baseline" => account with { PerformanceBaseline = null },
            "stale-snapshot" => account with { Snapshot = account.Snapshot with { ObservedAt = LiveRiskTests.Now.AddSeconds(-31) } },
            "initial-holdings" => account with { Snapshot = account.Snapshot with { CashBalance = 900, Positions = [new("BTC-THB", .001m, 0, 100000)] } },
            "initial-reserve" => account with { Snapshot = account.Snapshot with { CashReserved = 10 } },
            _ => account with { FirstActivatedAt = LiveRiskTests.Now.AddDays(-1), Snapshot = account.Snapshot with { CashReserved = 1001 } }
        };
        var tx = Transaction(account);
        Assert.False(tx.Arm(Request, Preflight, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Account.Gate.Armed);
    }

    [Fact]
    public void Stop_is_only_cleared_by_separate_explicit_owner_confirmation()
    {
        var tx = Transaction(Seed() with { Gate = LiveRiskTests.Gate with { Armed = false, Stopped = true } });
        Assert.False(tx.Arm(Request, Preflight, LiveRiskTests.Now).Allowed);
        Assert.True(tx.Account.Gate.Stopped);
        Assert.True(tx.Arm(Request with { ClearStop = true }, Preflight, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Account.Gate.Stopped);
        Assert.Contains(tx.Account.Audit, e => e.Kind == "OwnerClearedStop");
    }

    [Fact]
    public void Valid_mandate_change_disarms_and_preserves_money_and_stop()
    {
        var tx = Transaction(Seed() with { Gate = LiveRiskTests.Gate with { Stopped = true } });
        var result = tx.SaveMandate(LiveRiskTests.Mandate with { Version = 2, MaximumOrderNotional = 500 }, LiveRiskTests.Now);
        Assert.True(result.Allowed);
        Assert.False(tx.Account.Gate.Armed);
        Assert.True(tx.Account.Gate.Stopped);
        Assert.False(tx.Account.Snapshot.Reconciled);
        Assert.Equal(2, tx.Account.Mandate.Version);
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
        Assert.Contains(tx.Account.Audit, e => e.Kind == "OwnerMandateSaved");
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("stale")]
    [InlineData("credentials")]
    [InlineData("account")]
    public void Invalid_mandate_does_not_replace_saved_limits(string failure)
    {
        var mandate = LiveRiskTests.Mandate with { Version = 2 };
        mandate = failure switch
        {
            "zero" => mandate with { MaximumDailyLoss = 0 },
            "stale" => mandate with { Version = 1 },
            "credentials" => mandate with { CredentialFingerprint = "other-key" },
            _ => mandate with { AccountId = new(Guid.Empty) }
        };
        var tx = Transaction();
        Assert.False(tx.SaveMandate(mandate, LiveRiskTests.Now).Allowed);
        Assert.Equal(1, tx.Account.Mandate.Version);
        Assert.Equal(100, tx.Account.Mandate.MaximumDailyLoss);
    }

    private static LiveAccountTransaction Transaction(LiveAccountState? state = null)
        => new(state ?? Seed(), (_, _) => Task.CompletedTask);

    private static LiveAccountState Seed() => new(LiveRiskTests.Account, LiveRiskTests.Mandate,
        LiveRiskTests.Gate with { Armed = false }, LiveRiskTests.Snapshot, [], [], [])
    {
        PerformanceBaseline = new(new(2026, 9, 27), 1000, 1000)
    };
    private static LiveArmRequest Request => new(LiveRiskTests.Account, "owner-confirmed-account", 1, 1, true, false);
    private static LiveActivationEvidence Preflight => new(LiveRiskTests.Account, "owner-confirmed-account", "key-fingerprint",
        1, 1, LiveRiskTests.Now, true, true, true, true);
}
