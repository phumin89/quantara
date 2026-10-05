using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardPaperFundingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-paper-migration-" + Guid.NewGuid().ToString("N"));
    private static DateTimeOffset At(string date) => DateTimeOffset.Parse(date + "T12:00:00+07:00");

    [Fact]
    public void Duplicate_initializer_that_started_before_other_tick_does_not_halt_funding()
    {
        var treasury = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        var funding = treasury.Advance(At("2026-09-09"));
        var firstStarted = At("2026-09-09").AddSeconds(1);
        var laterStarted = firstStarted.AddMilliseconds(20);
        var seeded = ForwardPaperFunding.Synchronize(null, funding, null, laterStarted, 100000m);
        var repeated = ForwardPaperFunding.Synchronize(seeded, funding, null, firstStarted, 100000m);
        Assert.Same(seeded, repeated);
        Assert.Equal(100000m, ForwardPaperLedger.Snapshot(repeated).Cash);
    }

    [Fact]
    public void Old_paper_journal_checksum_and_history_survive_initial_capital_extension()
    {
        const string document = "{\"version\":2,\"fundingAccountId\":\"11111111-1111-1111-1111-111111111111\",\"events\":[{\"at\":\"2026-09-09T12:00:00+07:00\",\"kind\":\"FUNDING\",\"credit\":{\"salaryDate\":\"2026-09-09\",\"amount\":10,\"creditedAt\":\"2026-09-09T12:00:00+07:00\"},\"order\":null,\"fill\":null,\"mark\":null}]}";
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(document)));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "old-paper.json");
        File.WriteAllText(path, "{\"hash\":\"" + hash + "\",\"account\":" + document + "}");
        var oldBytes = File.ReadAllBytes(path);
        var store = new JsonForwardPaperStore(path);
        var old = store.Read()!;
        Assert.Equal(10m, ForwardPaperLedger.Snapshot(old).Cash);
        Assert.Equal(oldBytes, File.ReadAllBytes(path));
        var next = store.Update(current => ForwardPaperLedger.InitializeCapital(current!, 100000m, At("2026-09-10")));
        Assert.Equal(old.Events[0], next.Events[0]);
        Assert.Equal(100010m, ForwardPaperLedger.Snapshot(new JsonForwardPaperStore(path).Read()!).Cash);
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.Validate(next with
            { Events = [.. next.Events, next.Events[^1]] }));
    }

    [Fact]
    public void Explicit_initial_capital_survives_restart_without_faking_salary_or_reserve()
    {
        var treasury = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        var funding = treasury.Advance(At("2026-09-09"));
        var treasuryBytes = File.ReadAllBytes(Path.Combine(root, "funding.json"));
        var path = Path.Combine(root, "paper.json");
        var store = new JsonForwardPaperStore(path);
        var paper = store.Update(current => ForwardPaperFunding.Synchronize(current, funding, null, At("2026-09-09"), 100000m));
        var snapshot = ForwardPaperLedger.Snapshot(paper);
        Assert.Equal(100000m, snapshot.Cash);
        Assert.Equal(100000m, snapshot.Equity);
        Assert.Equal(100000m, snapshot.TotalContributed);
        Assert.Equal(0m, snapshot.TotalPnl);
        Assert.Equal(0m, ForwardPaperFunding.Pending(paper, funding));
        Assert.Null(Assert.Single(paper.Events).Credit);
        Assert.Equal(treasuryBytes, File.ReadAllBytes(Path.Combine(root, "funding.json")));
        var saved = File.ReadAllBytes(path);
        var restarted = new JsonForwardPaperStore(path);
        restarted.Update(current => ForwardPaperFunding.Synchronize(current, funding, null, At("2026-09-10"), 100000m));
        Assert.Equal(saved, File.ReadAllBytes(path));
        // Disabling the setting later never removes saved capital or reclassifies it as salary.
        Assert.Equal(100000m, ForwardPaperLedger.Snapshot(ForwardPaperFunding.Synchronize(restarted.Read(), funding,
            null, At("2026-09-10"))).Cash);
        funding = treasury.Advance(At("2026-11-30"));
        paper = restarted.Update(current => ForwardPaperFunding.Synchronize(current, funding, null, At("2026-11-30"), 100000m));
        Assert.Equal(108295m, ForwardPaperLedger.Snapshot(paper).TotalContributed);
        Assert.Equal(65175m, funding.EmergencyBalance);
        Assert.Equal(0m, ForwardPaperFunding.Pending(paper, funding));
        Assert.Throws<InvalidDataException>(() => restarted.Update(current =>
            ForwardPaperFunding.Synchronize(current, funding, null, At("2026-12-01"), 200000m)));
        Assert.Equal(108295m, ForwardPaperLedger.Snapshot(restarted.Read()!).Cash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_seed_is_not_added(decimal amount)
    {
        var account = ForwardPaperLedger.Open(Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.InitializeCapital(account, amount, At("2026-09-09")));
        Assert.Empty(account.Events);
    }

    [Fact]
    public void Seed_is_not_a_topup_for_a_portfolio_that_already_started_trading()
    {
        var account = ForwardPaperLedger.Open(Guid.NewGuid());
        account = ForwardPaperLedger.Submit(account, new(Guid.NewGuid(), "BTC-THB", TradingCompany.Domain.OrderSide.Buy,
            .001m, At("2026-09-09"), new()), At("2026-09-09"));
        Assert.Throws<InvalidDataException>(() => ForwardPaperLedger.InitializeCapital(account, 100000m, At("2026-09-10")));
        Assert.Equal(0m, ForwardPaperLedger.Snapshot(account).Cash);
    }

    [Fact]
    public void Migration_preserves_v1_bytes_credit_identity_and_zero_profit()
    {
        var treasury = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        treasury.Advance(At("2026-09-09"));
        var funding = treasury.Advance(At("2026-11-30"));
        var legacyPath = Path.Combine(root, "legacy.json");
        var legacy = new JsonForwardInvestmentStore(legacyPath).Synchronize(funding, At("2026-12-01"));
        var bytes = File.ReadAllBytes(legacyPath);
        var store = new JsonForwardPaperStore(Path.Combine(root, "execution.json"));
        var paper = store.Update(current => ForwardPaperFunding.Synchronize(current, funding, legacy, At("2026-12-02")));
        Assert.Equal(bytes, File.ReadAllBytes(legacyPath));
        Assert.Equal(legacy.Credits[0], Assert.Single(paper.Events).Credit);
        Assert.Equal(8295m, ForwardPaperLedger.Snapshot(paper).Cash);
        Assert.Equal(0m, ForwardPaperLedger.Snapshot(paper).TotalPnl);
        paper = store.Update(current => ForwardPaperFunding.Synchronize(current, funding, legacy, At("2026-12-03")));
        Assert.Single(paper.Events);
        Assert.Equal(0m, ForwardPaperFunding.Pending(paper, funding));
        Assert.Throws<InvalidDataException>(() => ForwardPaperFunding.Synchronize(paper,
            funding with { AccountId = Guid.NewGuid() }, legacy, At("2026-12-03")));
    }

    [Fact]
    public void Empty_treasury_never_imports_demo_money_and_source_rollback_fails()
    {
        var treasury = new JsonForwardFundingStore(Path.Combine(root, "funding.json"));
        var empty = treasury.Advance(At("2026-09-09"));
        var paper = ForwardPaperFunding.Synchronize(null, empty, null, At("2026-09-09"));
        Assert.Equal(0m, ForwardPaperLedger.Snapshot(paper).Cash);
        var funded = treasury.Advance(At("2026-11-30"));
        paper = ForwardPaperFunding.Synchronize(paper, funded, null, At("2026-11-30"));
        Assert.Throws<InvalidDataException>(() => ForwardPaperFunding.Pending(paper, empty));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
