using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveRiskTests
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(7));
    internal static readonly LiveAccountId Account = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    internal static LiveMandate Mandate => new(Account, 1, "key-fingerprint", 1000m, 800m, 2000m,
        100m, .20m, .80m, 3, .01m, .003m, ["BTC-THB", "ETH-THB", "SOL-THB"], null);
    internal static LiveSnapshot Snapshot => new(Account, 1, Now, 1000m, 0m, 0m, [], 1000m, 0m, 0m, 0m, true,
        [new("BTC-THB", 99900m, 100000m, Now)]);
    internal static LiveIntent Intent => new(Account, Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"), TradingMode.Live, "BTC-THB", OrderSide.Buy,
        .004m, 100000m, 1, 1, "evidence");
    internal static LiveGateState Gate => new(true, false, "key-fingerprint", true, 1);

    [Fact] public void Valid_live_intent_passes_without_mutating_snapshot()
    {
        var snapshot = Snapshot;
        Assert.True(LiveRiskPolicy.Evaluate(Intent, snapshot, Mandate, Gate, Now).Allowed);
        Assert.Equal(1000m, snapshot.CashBalance);
    }

    [Fact] public void Three_verified_positions_fit_three_slots_while_two_slots_reject_the_third()
    {
        var snapshot = Snapshot with { CashBalance = 980, Positions = [new("BTC-THB", .0001m, 0, 100000), new("ETH-THB", .01m, 0, 1000)],
            Quotes = [new("SOL-THB", 9999, 10000, Now)] };
        var intent = Intent with { Symbol = "SOL-THB", Quantity = .01m, LimitPrice = 10000 };
        Assert.True(LiveRiskPolicy.Evaluate(intent, snapshot, Mandate with { MaximumOpenPositions = 3 }, Gate, Now).Allowed);
        Assert.Contains("MAX_OPEN_POSITIONS_REACHED", LiveRiskPolicy.Evaluate(intent, snapshot,
            Mandate with { MaximumOpenPositions = 2 }, Gate, Now).Violations);
    }

    [Theory]
    [InlineData(TradingMode.Sandbox)] [InlineData((TradingMode)99)]
    public void Non_live_intent_is_never_authorized(TradingMode mode)
        => Denied("MODE_MISMATCH", intent: Intent with { Mode = mode });

    [Fact] public void Missing_mandate_fails_closed() => Denied("INVALID_MANDATE", mandate: null, missingMandate: true);
    [Fact] public void Zero_allocation_fails_closed() => Denied("INVALID_MANDATE", mandate: Mandate with { CapitalAllocation = 0 });
    [Fact] public void Invalid_fraction_fails_closed() => Denied("INVALID_MANDATE", mandate: Mandate with { MaximumDrawdown = 1.1m });
    [Fact] public void Unconfigured_owner_limits_fail_closed() => Denied("INVALID_MANDATE", mandate: Mandate with { MaximumDailyTurnover = 0 });
    [Fact] public void Wrong_account_cannot_use_another_accounts_money()
        => Denied("ACCOUNT_MISMATCH", intent: Intent with { AccountId = new(Guid.Parse("44444444-4444-4444-4444-444444444444")) });
    [Fact] public void Changed_snapshot_invalidates_intent() => Denied("STALE_APPROVAL", snapshot: Snapshot with { Version = 2 });
    [Fact] public void Changed_mandate_invalidates_intent() => Denied("STALE_APPROVAL", mandate: Mandate with { Version = 2 });
    [Fact] public void Changed_credentials_disarm_execution() => Denied("ACCOUNT_BINDING_CHANGED", gate: Gate with { CredentialFingerprint = "new-key" });
    [Fact] public void Disarmed_account_does_not_trade() => Denied("NOT_ARMED", gate: Gate with { Armed = false });
    [Fact] public void Unverified_isolation_does_not_trade() => Denied("ISOLATION_REQUIRED", gate: Gate with { IsolationVerified = false });
    [Fact] public void Stop_blocks_sells_too()
        => Denied("STOPPED", gate: Gate with { Stopped = true }, intent: Intent with { Side = OrderSide.Sell });
    [Fact] public void Unreconciled_balances_are_not_buying_power() => Denied("RECONCILIATION_REQUIRED", snapshot: Snapshot with { Reconciled = false });
    [Fact] public void Future_snapshot_is_invalid() => Denied("STALE_SNAPSHOT", snapshot: Snapshot with { ObservedAt = Now.AddSeconds(1) });
    [Theory] [InlineData(30, true)] [InlineData(31, false)]
    public void Snapshot_age_boundary(int seconds, bool allowed)
        => Assert.Equal(allowed, LiveRiskPolicy.Evaluate(Intent, Snapshot with { ObservedAt = Now.AddSeconds(-seconds) }, Mandate, Gate, Now).Allowed);
    [Fact] public void Pending_orders_reserve_cash() => Denied("INSUFFICIENT_CASH", snapshot: Snapshot with { CashReserved = 700, PendingBuyNotional = 700 });
    [Fact] public void Extra_broker_cash_does_not_enlarge_allocation()
        => Denied("CAPITAL_ALLOCATION_EXCEEDED", intent: Intent with { Quantity = .011m },
            snapshot: Snapshot with { CashBalance = 10000, Equity = 10000 }, mandate: Mandate with { MaximumOrderNotional = 5000 });
    [Fact] public void Fees_count_towards_order_limit()
        => Denied("MAX_ORDER_NOTIONAL_EXCEEDED", intent: Intent with { Quantity = .008m });
    [Fact] public void Pending_exposure_counts_towards_portfolio_cap()
        => Denied("MAX_PORTFOLIO_EXPOSURE_EXCEEDED", snapshot: Snapshot with { PendingBuyNotional = 450, CashReserved = 451.35m });
    [Fact] public void Daily_turnover_counts_new_fees()
        => Denied("DAILY_TURNOVER_EXCEEDED", snapshot: Snapshot with { DailyTurnover = 1600 });
    [Fact] public void Cannot_sell_unowned_coins() => Denied("INSUFFICIENT_OWNED_QUANTITY", intent: Intent with { Side = OrderSide.Sell });
    [Fact] public void Reserved_coins_cannot_be_sold_twice()
        => Denied("INSUFFICIENT_OWNED_QUANTITY", intent: Intent with { Side = OrderSide.Sell },
            snapshot: Snapshot with { CashBalance = 500, Positions = [new("BTC-THB", .005m, .002m, 100000m)] });
    [Fact] public void Owned_sell_may_reduce_exposure_after_loss_gate()
    {
        var snapshot = Snapshot with { CashBalance = 500, Positions = [new("BTC-THB", .005m, 0, 100000m)], DailyLoss = 150 };
        Assert.True(LiveRiskPolicy.Evaluate(Intent with { Side = OrderSide.Sell }, snapshot, Mandate, Gate, Now).Allowed);
    }
    [Fact] public void Daily_loss_blocks_new_buys() => Denied("MAX_DAILY_LOSS_REACHED", snapshot: Snapshot with { DailyLoss = 100 });
    [Fact] public void Unsupported_symbol_cannot_trade() => Denied("SYMBOL_NOT_ALLOWED", intent: Intent with { Symbol = "DOGE-THB" });
    [Fact] public void Invalid_side_cannot_be_treated_as_a_buy() => Denied("INVALID_INTENT", intent: Intent with { Side = (OrderSide)7 });
    [Fact] public void Decimal_overflow_fails_closed() => Denied("INVALID_INTENT", intent: Intent with { Quantity = decimal.MaxValue });
    [Fact] public void Negative_balances_fail_closed() => Denied("INVALID_SNAPSHOT", snapshot: Snapshot with { CashBalance = -1 });
    [Fact] public void Expired_mandate_does_not_trade() => Denied("MANDATE_EXPIRED", mandate: Mandate with { ExpiresAt = Now });
    [Fact] public void Buy_limit_cannot_exceed_owner_slippage()
        => Denied("PRICE_BOUND_EXCEEDED", intent: Intent with { LimitPrice = 101001 });
    [Fact] public void Missing_or_stale_quote_does_not_trade()
        => Denied("QUOTE_UNAVAILABLE", snapshot: Snapshot with { Quotes = [new("BTC-THB", 99900, 100000, Now.AddSeconds(-31))] });
    [Fact] public void New_position_limit_is_enforced()
        => Denied("MAX_OPEN_POSITIONS_REACHED", mandate: Mandate with { MaximumOpenPositions = 1 },
            snapshot: Snapshot with { CashBalance = 900, Positions = [new("ETH-THB", .001m, 0, 100000)] });
    [Fact] public void Inflated_equity_is_not_accepted() => Denied("INVALID_SNAPSHOT", snapshot: Snapshot with { Equity = 10000 });

    private static void Denied(string code, LiveIntent? intent = null, LiveSnapshot? snapshot = null,
        LiveMandate? mandate = null, LiveGateState? gate = null, bool missingMandate = false)
    {
        var result = LiveRiskPolicy.Evaluate(intent ?? Intent, snapshot ?? Snapshot, missingMandate ? null : mandate ?? Mandate, gate ?? Gate, Now);
        Assert.False(result.Allowed);
        Assert.Contains(code, result.Violations);
    }
}
