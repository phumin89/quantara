using TradingCompany.Application;
using TradingCompany.Domain;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveAccountTransactionTests
{
    [Fact]
    public void Cancellation_cannot_target_an_order_created_under_different_credentials()
    {
        var original = Transaction(LiveRiskTests.Snapshot, LiveRiskTests.Mandate);
        original.Reserve(LiveRiskTests.Intent, LiveRiskTests.Now);
        original.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        original.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Acknowledged, "broker-order", LiveRiskTests.Now);
        var switched = new LiveAccountTransaction(original.Account with
        {
            Mandate = original.Account.Mandate with { CredentialFingerprint = "replacement-account-key" },
            Gate = original.Account.Gate with { CredentialFingerprint = "replacement-account-key", Armed = false }
        }, (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidOperationException>(() => switched.RequestCancellation(LiveRiskTests.Intent.Id, LiveRiskTests.Now));
        Assert.Equal(401.2m, switched.Account.Snapshot.CashReserved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Fill_without_broker_order_identity_cannot_mutate_a_dispatching_order(string? orderId)
    {
        var tx = Transaction(LiveRiskTests.Snapshot, LiveRiskTests.Mandate);
        tx.Reserve(LiveRiskTests.Intent, LiveRiskTests.Now);
        tx.RecordTransition(LiveRiskTests.Intent.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        var fill = new BrokerFill(LiveRiskTests.Account, "txn", orderId!, OrderSide.Buy, "BTC-THB", .001m, 100000, .25m, 1);
        Assert.Throws<InvalidDataException>(() => tx.ApplyFill(fill, LiveRiskTests.Now));
        Assert.Equal(1000m, tx.Account.Snapshot.CashBalance);
        Assert.Empty(tx.Account.Fills);
    }

    [Fact]
    public void Pending_buys_consume_position_slots_before_they_fill()
    {
        var snapshot = LiveRiskTests.Snapshot with { Quotes = [.. LiveRiskTests.Snapshot.Quotes, new("ETH-THB", 99900, 100000, LiveRiskTests.Now)] };
        var tx = Transaction(snapshot, LiveRiskTests.Mandate with { MaximumOpenPositions = 1 });
        Assert.Equal(LiveOrderState.Reserved, tx.Reserve(LiveRiskTests.Intent with { Quantity = .001m }, LiveRiskTests.Now).State);
        var second = tx.Reserve(LiveRiskTests.Intent with { Id = Guid.NewGuid(), Symbol = "ETH-THB", Quantity = .001m,
            SnapshotVersion = tx.Account.Snapshot.Version }, LiveRiskTests.Now);
        Assert.Equal(LiveOrderState.Rejected, second.State);
        Assert.Contains("MAX_OPEN_POSITIONS_REACHED", second.Violations);
    }

    [Fact]
    public void Pending_sells_consume_daily_turnover_before_they_fill()
    {
        var snapshot = LiveRiskTests.Snapshot with { CashBalance = 500, Positions = [new("BTC-THB", .005m, 0, 100000)] };
        var tx = Transaction(snapshot, LiveRiskTests.Mandate with { MaximumDailyTurnover = 350 });
        var intent = LiveRiskTests.Intent with { Side = OrderSide.Sell, Quantity = .003m };
        Assert.Equal(LiveOrderState.Reserved, tx.Reserve(intent, LiveRiskTests.Now).State);
        var second = tx.Reserve(intent with { Id = Guid.NewGuid(), Quantity = .001m,
            SnapshotVersion = tx.Account.Snapshot.Version }, LiveRiskTests.Now);
        Assert.Equal(LiveOrderState.Rejected, second.State);
        Assert.Contains("DAILY_TURNOVER_EXCEEDED", second.Violations);
    }

    [Fact]
    public void Fill_identity_is_scoped_to_symbol_and_side()
    {
        var snapshot = LiveRiskTests.Snapshot with { Quotes = [.. LiveRiskTests.Snapshot.Quotes, new("ETH-THB", 99900, 100000, LiveRiskTests.Now)] };
        var tx = Transaction(snapshot, LiveRiskTests.Mandate);
        var first = LiveRiskTests.Intent with { Quantity = .001m };
        tx.Reserve(first, LiveRiskTests.Now);
        tx.RecordTransition(first.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(first.Id, LiveOrderState.Acknowledged, "opaque", LiveRiskTests.Now);
        var second = first with { Id = Guid.NewGuid(), Symbol = "ETH-THB", SnapshotVersion = tx.Account.Snapshot.Version };
        tx.Reserve(second, LiveRiskTests.Now);
        tx.RecordTransition(second.Id, LiveOrderState.Dispatching, null, LiveRiskTests.Now);
        tx.RecordTransition(second.Id, LiveOrderState.Acknowledged, "opaque", LiveRiskTests.Now);
        tx.ApplyFill(new(LiveRiskTests.Account, "tx", "opaque", OrderSide.Buy, "ETH-THB", .001m, 100000, .25m, 1), LiveRiskTests.Now);
        Assert.Equal("ETH-THB", Assert.Single(tx.Account.Snapshot.Positions).Symbol);
        Assert.Equal(0, tx.ReadOrder(first.Id)!.FilledQuantity);
        Assert.Equal(.001m, tx.ReadOrder(second.Id)!.FilledQuantity);
    }

    private static LiveAccountTransaction Transaction(LiveSnapshot snapshot, LiveMandate mandate)
        => new(new(LiveRiskTests.Account, mandate, LiveRiskTests.Gate, snapshot, [], [], []), (_, _) => Task.CompletedTask);
}
