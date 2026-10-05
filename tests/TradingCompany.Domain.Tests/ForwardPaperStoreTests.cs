using TradingCompany.Application;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class ForwardPaperStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-paper-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(root, "paper.json");
    private static readonly DateTimeOffset Day = DateTimeOffset.Parse("2026-09-30T10:00:00Z");

    private static ForwardPaperAccount Funded(Guid? fundingId = null, decimal amount = 40000m)
        => ForwardPaperLedger.Fund(ForwardPaperLedger.Open(fundingId ?? Guid.NewGuid()),
            new(new DateOnly(2026, 9, 30), amount, Day));

    private static ForwardPaperIntent Order(Guid? id = null)
        => new(id ?? Guid.NewGuid(), "NVDA", OrderSide.Buy, 2m, Day.AddMinutes(1), new());

    private static ForwardPaperQuote Quote()
        => new("NVDA", 100m, "USD", 35m, Day.AddMinutes(2), Day.AddMinutes(2), Day, "controlled-fixture", 10m);

    [Fact]
    public void Empty_read_does_not_create_files_or_directories()
    {
        var store = new JsonForwardPaperStore(FilePath);

        Assert.Null(store.Read());
        Assert.False(File.Exists(FilePath));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Funding_order_and_filled_receipt_survive_reopen()
    {
        var order = Order();
        var store = new JsonForwardPaperStore(FilePath);
        store.Update(_ => ForwardPaperLedger.Submit(Funded(), order, Day.AddMinutes(1)));
        store.Update(account => ForwardPaperLedger.Execute(account!, order.Id, Quote(), Day.AddMinutes(3)));

        var reopened = new JsonForwardPaperStore(FilePath).Read()!;
        var snapshot = ForwardPaperLedger.Snapshot(reopened);
        Assert.Equal(40000m, snapshot.TotalContributed);
        Assert.Empty(snapshot.PendingOrders);
        Assert.Single(snapshot.Receipts);
        Assert.Equal(order.Id, snapshot.Receipts[0].OrderId);
    }

    [Fact]
    public async Task Concurrent_same_order_updates_create_one_fill()
    {
        var order = Order();
        var store = new JsonForwardPaperStore(FilePath);
        store.Update(_ => ForwardPaperLedger.Submit(Funded(), order, Day.AddMinutes(1)));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            store.Update(account => ForwardPaperLedger.Execute(account!, order.Id, Quote(), Day.AddMinutes(3))))));

        Assert.Single(ForwardPaperLedger.Snapshot(store.Read()!).Receipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_or_missing_initialized_file_fails_closed(bool deleteFile)
    {
        var store = new JsonForwardPaperStore(FilePath);
        store.Update(_ => Funded());
        if (deleteFile) File.Delete(FilePath);
        else File.WriteAllText(FilePath, "{broken");

        Assert.Throws<InvalidDataException>(() => store.Read());
        Assert.Throws<InvalidDataException>(() => store.Update(account => account!));
    }

    [Fact]
    public void Throwing_callback_leaves_prior_bytes_intact()
    {
        var store = new JsonForwardPaperStore(FilePath);
        store.Update(_ => Funded());
        var before = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidOperationException>(() => store.Update(_ => throw new InvalidOperationException("boom")));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Funding_identity_change_and_event_truncation_are_rejected()
    {
        var store = new JsonForwardPaperStore(FilePath);
        store.Update(_ => Funded());
        var before = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => store.Update(account => account! with { FundingAccountId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => store.Update(account => account! with { Events = [] }));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
