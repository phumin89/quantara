using System.Text.Json;
using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveOpeningBalanceTests
{
    private const string BrokerReference = "owner-confirmed-account";
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static LiveBaselineRequest BaselineRequest => new(LiveRiskTests.Account, 1, 1);
    private static LiveBrokerPosition[] OpeningPositions => [new("BTC-THB", .002m, 0), new("ETH-THB", .01m, 0)];
    private static LiveQuote[] Quotes => [new("BTC-THB", 100000m, 100000m, LiveRiskTests.Now),
        new("ETH-THB", 30000m, 30000m, LiveRiskTests.Now)];

    [Theory]
    [InlineData(500)]
    [InlineData(0)]
    public void Stable_bound_holdings_establish_full_equity_baseline_without_manufacturing_trades(decimal cash)
    {
        var account = Seed(cash);
        var tx = Transaction(account);
        var evidence = Evidence(cash);

        var result = tx.InitializeBaseline(BaselineRequest, evidence, evidence, LiveRiskTests.Now);

        Assert.True(result.Complete);
        Assert.Equal(new LivePerformanceBaseline(Today, cash + 500m, cash + 500m), tx.Account.PerformanceBaseline);
        Assert.Equal(cash, tx.Account.Snapshot.CashBalance);
        Assert.Equal(cash + 500m, tx.Account.Snapshot.Equity);
        Assert.True(tx.Account.Snapshot.Reconciled);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Null(tx.Account.FirstActivatedAt);
        Assert.Equal(account.Mandate, tx.Account.Mandate);
        Assert.Empty(tx.Account.Fills);
        Assert.Empty(tx.Account.Orders);
        AssertOpeningUnchanged(account.OpeningBalance!, tx.Account.OpeningBalance);
        Assert.Single(tx.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
    }

    [Fact]
    public void Matching_opening_preflight_allows_assessment_and_explicit_owner_arm_with_holdings()
    {
        var tx = Initialized();
        var opening = tx.Account.OpeningBalance!;
        var baseline = tx.Account.PerformanceBaseline;
        var request = ArmRequest(tx.Account);
        var preflight = Preflight(tx.Account);

        Assert.True(tx.AssessActivation(request, preflight, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Null(tx.Account.FirstActivatedAt);
        Assert.True(tx.Arm(request, preflight, LiveRiskTests.Now).Allowed);

        Assert.True(tx.Account.Gate.Armed);
        Assert.Equal(LiveRiskTests.Now, tx.Account.FirstActivatedAt);
        Assert.Equal(BrokerReference, tx.Account.BrokerAccountReference);
        Assert.Equal(baseline, tx.Account.PerformanceBaseline);
        AssertOpeningUnchanged(opening, tx.Account.OpeningBalance);
        Assert.Equal(500m, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Fills);
        Assert.Empty(tx.Account.Orders);
    }

    [Fact]
    public void Price_marks_change_equity_without_changing_immutable_opening_cash_or_quantities()
    {
        var tx = Initialized();
        var opening = tx.Account.OpeningBalance!;
        var evidence = Evidence() with
        {
            Quotes = [new("BTC-THB", 110000m, 110000m, LiveRiskTests.Now),
                new("ETH-THB", 33000m, 33000m, LiveRiskTests.Now)]
        };

        Assert.True(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(1050m, tx.Account.Snapshot.Equity);
        Assert.Equal(new LivePerformanceBaseline(Today, 1000m, 1050m), tx.Account.PerformanceBaseline);
        Assert.Equal(.002m, tx.Account.Snapshot.Positions.Single(p => p.Symbol == "BTC-THB").Quantity);
        Assert.Equal(.01m, tx.Account.Snapshot.Positions.Single(p => p.Symbol == "ETH-THB").Quantity);
        AssertOpeningUnchanged(opening, tx.Account.OpeningBalance);
        Assert.Empty(tx.Account.Orders);
        Assert.Empty(tx.Account.Fills);
        Assert.True(tx.AssessActivation(ArmRequest(tx.Account), Preflight(tx.Account), LiveRiskTests.Now).Allowed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cash")]
    [InlineData("quantity")]
    [InlineData("fingerprint")]
    [InlineData("broker-reference")]
    [InlineData("duplicate")]
    [InlineData("unsupported")]
    [InlineData("reserved-coins")]
    public void Invalid_opening_cannot_establish_baseline_or_change_money(string defect)
    {
        var (account, evidence) = Defective(defect);
        var tx = Transaction(account);

        Assert.False(tx.InitializeBaseline(BaselineRequest, evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Null(tx.Account.PerformanceBaseline);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Equal(account.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Equal(account.Snapshot.Positions, tx.Account.Snapshot.Positions);
        Assert.Equal(account.OpeningBalance, tx.Account.OpeningBalance);
        Assert.Empty(tx.Account.Fills);
        Assert.Empty(tx.Account.Orders);
        Assert.DoesNotContain(tx.Account.Audit, a => a.Kind == "InitialPerformanceBaselineEstablished");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cash")]
    [InlineData("quantity")]
    [InlineData("fingerprint")]
    [InlineData("broker-reference")]
    [InlineData("duplicate")]
    [InlineData("unsupported")]
    [InlineData("reserved-coins")]
    [InlineData("preflight-flag")]
    [InlineData("preflight-broker-reference")]
    public void Unsafe_opening_or_unverified_preflight_blocks_assessment_and_arming(string defect)
    {
        var (account, _) = Defective(defect);
        account = account with
        {
            Snapshot = account.Snapshot with { Reconciled = true },
            PerformanceBaseline = new(Today, account.Snapshot.Equity, account.Snapshot.Equity)
        };
        var tx = Transaction(account);
        var preflight = Preflight(account) with { OpeningBalanceVerified = defect != "preflight-flag" };
        var request = ArmRequest(account);
        if (defect == "preflight-broker-reference")
        {
            preflight = preflight with { BrokerAccountReference = "another-account" };
            request = request with { BrokerAccountReference = "another-account" };
        }

        Assert.False(tx.AssessActivation(request, preflight, LiveRiskTests.Now).Allowed);
        Assert.False(tx.Arm(request, preflight, LiveRiskTests.Now).Allowed);

        Assert.False(tx.Account.Gate.Armed);
        Assert.Null(tx.Account.FirstActivatedAt);
        Assert.Equal(account.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Equal(account.Snapshot.Positions, tx.Account.Snapshot.Positions);
        Assert.Equal(account.OpeningBalance, tx.Account.OpeningBalance);
        Assert.Equal(account.PerformanceBaseline, tx.Account.PerformanceBaseline);
        Assert.Empty(tx.Account.Orders);
        Assert.Empty(tx.Account.Fills);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("coins")]
    public void Post_initialization_deposit_cannot_be_absorbed_into_opening_balance_or_baseline(string deposit)
    {
        var tx = Initialized();
        var before = tx.Account;
        var evidence = Evidence();
        evidence = deposit == "cash" ? evidence with { CashBalance = 501m } : evidence with
        {
            Positions = [new("BTC-THB", .003m, 0), new("ETH-THB", .01m, 0)]
        };

        Assert.False(tx.Reconcile(evidence, evidence, LiveRiskTests.Now).Complete);

        Assert.Equal(before.Snapshot.CashBalance, tx.Account.Snapshot.CashBalance);
        Assert.Equal(before.Snapshot.Positions, tx.Account.Snapshot.Positions);
        Assert.Equal(before.PerformanceBaseline, tx.Account.PerformanceBaseline);
        AssertOpeningUnchanged(before.OpeningBalance!, tx.Account.OpeningBalance);
        Assert.False(tx.Account.Snapshot.Reconciled);
        Assert.False(tx.Account.Gate.Armed);
        Assert.Empty(tx.Account.Orders);
        Assert.Empty(tx.Account.Fills);
    }

    [Fact]
    public void Legacy_cash_only_record_without_opening_property_still_initializes_and_arms()
    {
        var legacy = new LiveAccountState(LiveRiskTests.Account, LiveRiskTests.Mandate,
            LiveRiskTests.Gate with { Armed = false }, LiveRiskTests.Snapshot with { Reconciled = false }, [], [], []);
        var saved = JsonSerializer.SerializeToNode(legacy)!.AsObject();
        saved.Remove(nameof(LiveAccountState.OpeningBalance));
        var restored = JsonSerializer.Deserialize<LiveAccountState>(saved.ToJsonString())!;
        Assert.Null(restored.OpeningBalance);
        var tx = Transaction(restored);
        var evidence = Evidence() with { CashBalance = 1000m, Positions = [], Quotes = LiveRiskTests.Snapshot.Quotes };

        Assert.True(tx.InitializeBaseline(BaselineRequest, evidence, evidence, LiveRiskTests.Now).Complete);
        var preflight = new LiveActivationEvidence(LiveRiskTests.Account, BrokerReference, "key-fingerprint",
            tx.Account.Mandate.Version, tx.Account.Snapshot.Version, LiveRiskTests.Now, true, true, true, true);
        Assert.False(preflight.OpeningBalanceVerified);
        Assert.True(tx.AssessActivation(ArmRequest(tx.Account), preflight, LiveRiskTests.Now).Allowed);
        Assert.True(tx.Arm(ArmRequest(tx.Account), preflight, LiveRiskTests.Now).Allowed);
        Assert.Equal(new LivePerformanceBaseline(Today, 1000m, 1000m), tx.Account.PerformanceBaseline);
        Assert.Null(tx.Account.OpeningBalance);
        Assert.Empty(tx.Account.Snapshot.Positions);
        Assert.Empty(tx.Account.Orders);
        Assert.Empty(tx.Account.Fills);
    }

    private static LiveAccountState Seed(decimal cash = 500m)
        => new(LiveRiskTests.Account, LiveRiskTests.Mandate, LiveRiskTests.Gate with { Armed = false },
            LiveRiskTests.Snapshot with
            {
                CashBalance = cash,
                Positions = [new("BTC-THB", .002m, 0, 100000m), new("ETH-THB", .01m, 0, 30000m)],
                Equity = cash + 500m,
                Quotes = Quotes,
                Reconciled = false
            }, [], [], [])
        {
            BrokerAccountReference = BrokerReference,
            OpeningBalance = new(LiveRiskTests.Now, "key-fingerprint", BrokerReference, cash, OpeningPositions)
        };

    private static LiveBrokerEvidence Evidence(decimal cash = 500m)
        => new(LiveRiskTests.Account, "key-fingerprint", LiveRiskTests.Now, true, cash, 0m,
            OpeningPositions, [], [], Quotes);

    private static LiveAccountTransaction Transaction(LiveAccountState account)
        => new(account, (_, _) => Task.CompletedTask);

    private static LiveAccountTransaction Initialized()
    {
        var tx = Transaction(Seed());
        var evidence = Evidence();
        Assert.True(tx.InitializeBaseline(BaselineRequest, evidence, evidence, LiveRiskTests.Now).Complete);
        return tx;
    }

    private static LiveArmRequest ArmRequest(LiveAccountState account)
        => new(account.AccountId, BrokerReference, account.Mandate.Version, account.Snapshot.Version, true, false);

    private static LiveActivationEvidence Preflight(LiveAccountState account)
        => new(account.AccountId, BrokerReference, "key-fingerprint", account.Mandate.Version,
            account.Snapshot.Version, LiveRiskTests.Now, true, true, true, false) { OpeningBalanceVerified = true };

    private static (LiveAccountState Account, LiveBrokerEvidence Evidence) Defective(string defect)
    {
        var account = Seed();
        var evidence = Evidence();
        var opening = account.OpeningBalance!;
        account = defect switch
        {
            "missing" => account with { OpeningBalance = null },
            "cash" => account with { OpeningBalance = opening with { CashBalance = 501m } },
            "quantity" => account with { OpeningBalance = opening with { Positions = [new("BTC-THB", .003m, 0), new("ETH-THB", .01m, 0)] } },
            "fingerprint" => account with { OpeningBalance = opening with { CredentialFingerprint = "another-key" } },
            "broker-reference" => account with { OpeningBalance = opening with { BrokerAccountReference = "another-account" } },
            "duplicate" => account with { OpeningBalance = opening with { Positions = [.. opening.Positions, opening.Positions[0]] } },
            _ => account
        };
        if (defect is "unsupported" or "reserved-coins")
        {
            var position = defect == "unsupported" ? new LiveBrokerPosition("DOGE-THB", .002m, 0)
                : new LiveBrokerPosition("BTC-THB", .002m, .001m);
            var changedPositions = new LiveBrokerPosition[] { position, opening.Positions[1] };
            account = account with
            {
                OpeningBalance = opening with { Positions = changedPositions },
                Snapshot = account.Snapshot with { Positions = [new(position.Symbol, position.Quantity,
                    position.ReservedQuantity, 100000m), account.Snapshot.Positions[1]] }
            };
            evidence = evidence with { Positions = changedPositions, Quotes = [Quotes[0] with { Symbol = position.Symbol }, Quotes[1]] };
        }
        return (account, evidence);
    }

    private static void AssertOpeningUnchanged(LiveOpeningBalance expected, LiveOpeningBalance? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ObservedAt, actual.ObservedAt);
        Assert.Equal(expected.CredentialFingerprint, actual.CredentialFingerprint);
        Assert.Equal(expected.BrokerAccountReference, actual.BrokerAccountReference);
        Assert.Equal(expected.CashBalance, actual.CashBalance);
        Assert.Equal(expected.Positions, actual.Positions);
    }
}
