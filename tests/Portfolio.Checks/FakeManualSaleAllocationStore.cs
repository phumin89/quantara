using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public sealed class FakeManualSaleAllocationStore(string accountKey, FakeTradingStore trading)
    : IManualSaleAllocationStore
{
    public ManualSaleAllocationJournal Journal { get; set; } = new(accountKey, 0, []);

    public bool FailBeforeCommit { get; set; }

    public bool FailAfterCommit { get; set; }

    public bool LoseClaim { get; set; }

    public int AppendCalls { get; private set; }

    public Task<ManualSaleAllocationJournal> Read(CancellationToken cancellationToken) => Task.FromResult(this.Journal);

    public Task<bool> Append(
        ManualSaleAllocationReceipt receipt,
        long expectedJournalRevision,
        long expectedTradingRevision,
        CancellationToken cancellationToken,
        TradingExternalSale? retirement = null
    )
    {
        this.AppendCalls++;
        if (
            this.LoseClaim
            || this.Journal.Revision != expectedJournalRevision
            || trading.State.Revision != expectedTradingRevision
        )
        {
            return Task.FromResult(false);
        }

        if (this.FailBeforeCommit)
        {
            throw new IOException("Synthetic journal failure before commit.");
        }

        var retained = this.Journal.Receipts.SingleOrDefault(item =>
            item.Allocation.Lot.LotKey == receipt.Allocation.Lot.LotKey
            && item.Allocation.Execution.ExecutionId == receipt.Allocation.Execution.ExecutionId
        );
        if (retained is not null && (retirement is null || retained.Allocation != receipt.Allocation))
        {
            throw new InvalidDataException("Synthetic retained attribution conflict.");
        }

        var next = this.Journal with
        {
            Revision = this.Journal.Revision + (retained is null ? 1 : 0),
            Receipts = retained is null ? this.Journal.Receipts.Append(receipt).ToArray() : this.Journal.Receipts,
        };
        next.Validate(accountKey);
        var nextTrading = retirement is null
            ? null
            : TradingManualSaleInventory.Retire(trading.State, retirement, accountKey, retirement.RecordedAt);
        this.Journal =
            JsonSerializer.Deserialize<ManualSaleAllocationJournal>(JsonSerializer.Serialize(next))
            ?? throw new InvalidDataException("Synthetic journal was not retained.");
        if (nextTrading is not null)
        {
            trading.State =
                JsonSerializer.Deserialize<TradingState>(JsonSerializer.Serialize(nextTrading))
                ?? throw new InvalidDataException("Synthetic inventory retirement was not retained.");
            trading.History.Add(trading.State);
        }

        if (this.FailAfterCommit)
        {
            throw new IOException("Synthetic uncertain journal acknowledgement.");
        }

        return Task.FromResult(true);
    }
}
