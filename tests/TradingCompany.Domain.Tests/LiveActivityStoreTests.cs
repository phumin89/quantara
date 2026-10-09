using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.LiveExecution;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class LiveActivityStoreTests
{
    private static readonly LiveAccountId Account = new(Guid.Parse("ca692cd6-de98-4146-aa92-7cbb2a0df92a"));
    private static LiveCompanyActivity Activity(DateTimeOffset now) => new(new(TradingMode.Live, Account.Value.ToString("N")),
        Guid.Parse("d79eb810-8e2b-4539-bfe7-bd82e00429fb"), now, [new("ceo", "working", now)]);

    [Fact]
    public void Account_bound_snapshot_expires_without_heartbeat_and_cannot_be_restored_by_an_older_packet()
    {
        var clock = new Clock();
        var store = new LiveActivityStore(clock);
        var started = Activity(clock.Now);
        Assert.True(store.Save(started, Account));
        Assert.Null(store.Read(new(Guid.Parse("b91b2cfe-d690-468a-a0f9-1c9b649cbe81"))));
        clock.Now += TimeSpan.FromSeconds(15);
        var finished = started with { ObservedAt = clock.Now, Roles = [new("ceo", "completed", clock.Now)] };
        Assert.True(store.Save(finished, Account));
        Assert.False(store.Save(started, Account));
        Assert.Equal("completed", Assert.Single(store.Read(Account)!.Roles).Status);
        clock.Now += LiveActivityValidation.Lifetime;
        Assert.Null(store.Read(Account));
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("account")]
    [InlineData("role")]
    [InlineData("status")]
    [InlineData("future")]
    [InlineData("stale")]
    [InlineData("duplicate")]
    [InlineData("parallel")]
    [InlineData("missing-meeting")]
    [InlineData("future-change")]
    public void Untrusted_telemetry_is_rejected_without_changing_existing_activity(string defect)
    {
        var clock = new Clock();
        var store = new LiveActivityStore(clock);
        var valid = Activity(clock.Now);
        Assert.True(store.Save(valid, Account));
        clock.Now += TimeSpan.FromSeconds(1);
        var invalid = Activity(clock.Now);
        invalid = defect switch
        {
            "paper" => invalid with { AccountContext = new(TradingMode.Sandbox, Account.Value.ToString("N")) },
            "account" => invalid with { AccountContext = new(TradingMode.Live, Guid.NewGuid().ToString("N")) },
            "role" => invalid with { Roles = [new("invented", "working", clock.Now)] },
            "status" => invalid with { Roles = [new("ceo", "trading", clock.Now)] },
            "future" => invalid with { ObservedAt = clock.Now.AddSeconds(6) },
            "stale" => invalid with { ObservedAt = clock.Now.AddSeconds(-45) },
            "duplicate" => invalid with { Roles = [new("ceo", "completed", clock.Now), new("ceo", "working", clock.Now)] },
            "parallel" => invalid with { Roles = [new("ceo", "working", clock.Now), new("market-research", "working", clock.Now)] },
            "missing-meeting" => invalid with { MeetingId = null },
            _ => invalid with { Roles = [new("ceo", "working", clock.Now.AddSeconds(1))] }
        };
        Assert.False(store.Save(invalid, Account));
        Assert.Equal(valid.ObservedAt, store.Read(Account)!.ObservedAt);
    }

    [Fact]
    public void Dynamic_market_activity_and_history_issues_accept_payload_bound_without_expanding_trading_membership()
    {
        var clock = new Clock();
        var symbols = Enumerable.Range(0, LiveMarketSymbol.MaximumPayloadMarkets).Select(i => $"M{i:0000}-THB").ToArray();
        var valid = Activity(clock.Now) with { ResearchSymbols = symbols, TradingSymbols = ["M0000-THB"],
            Issues = symbols.Select(s => new LiveMeetingIssue("MARKET_HISTORY_UNAVAILABLE", s)).ToArray() };
        Assert.True(LiveActivityValidation.IsValid(valid, Account, clock.Now));
        Assert.False(LiveActivityValidation.IsValid(valid with { TradingSymbols = ["XRP-THB"] }, Account, clock.Now));
        Assert.False(LiveActivityValidation.IsValid(valid with { ResearchSymbols = ["DOGE-THB\nIGNORE"] }, Account, clock.Now));
        Assert.False(LiveActivityValidation.IsValid(valid with { ResearchSymbols = [.. symbols, "XRP-THB"] }, Account, clock.Now));
        Assert.False(LiveMeetingIssues.IsValid([new("MARKET_HISTORY_UNAVAILABLE", "DOGE-THB\nIGNORE")]));
        Assert.False(LiveMeetingIssues.IsValid([.. valid.Issues!, new("PUBLIC_RESEARCH_UNAVAILABLE")]));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 6, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
