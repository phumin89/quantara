using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Rehearsal;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class RehearsalVerificationTests
{
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-09-26T07:00:00Z");
    private static ForwardPaperAccount Funded() => ForwardPaperLedger.Fund(ForwardPaperLedger.Open(Guid.NewGuid()), new(new(2026, 9, 26), 10000m, Day));

    [Theory]
    [InlineData("RECONCILIATION_REQUIRED")]
    [InlineData("PROVIDER_FAILURE")]
    public void Unchanged_balances_do_not_prove_a_healthy_restart(string status)
    {
        var account = Funded();
        Assert.False(RehearsalVerification.Passed(account, account, true, status, 8, 8));
    }

    [Fact]
    public void Legitimate_delayed_fill_after_restart_preserves_valid_history()
    {
        var order = new ForwardPaperIntent(Guid.NewGuid(), "BTC-THB", OrderSide.Buy, .001m, Day.AddSeconds(1), new());
        var before = ForwardPaperLedger.Submit(Funded(), order, order.DecisionAt);
        var after = ForwardPaperLedger.Execute(before, order.Id,
            new("BTC-THB", 1000000m, "THB", 1m, Day.AddSeconds(2), Day.AddSeconds(2), Day, "test", FeeRate: .0025m), Day.AddSeconds(2));
        Assert.True(RehearsalVerification.Passed(before, after, true, "COMPLETED", 8, 8));
        Assert.False(RehearsalVerification.Passed(before, after, true, "COMPLETED", 8, 16));
        Assert.False(RehearsalVerification.Passed(before, after, false, "COMPLETED", 8, 8));
    }

    [Fact]
    public void Changed_or_duplicate_history_is_not_reconciliation()
    {
        var before = Funded();
        var changed = before with { Events = [before.Events[0] with { Credit = before.Events[0].Credit! with { Amount = 20000m } }] };
        Assert.False(RehearsalVerification.Passed(before, changed, true, "COMPLETED", 8, 8));
        var duplicate = before with { Events = [.. before.Events, before.Events[0]] };
        Assert.False(RehearsalVerification.Passed(before, duplicate, true, "COMPLETED", 8, 8));
    }
}
