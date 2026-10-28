namespace Quantara.Portfolio.Domain;

public sealed record ManualSaleAllocationJournal(
    string AccountKey,
    long Revision,
    IReadOnlyList<ManualSaleAllocationReceipt> Receipts
)
{
    public void Validate(string accountKey)
    {
        if (
            this.AccountKey != accountKey
            || this.Revision < 0
            || this.Receipts is null
            || this.Receipts.Count > 1024
            || this.Revision != this.Receipts.Count
            || this.Receipts.Any(receipt =>
                receipt is null
                || receipt.Allocation is null
                || receipt.Allocation.Execution is null
                || string.IsNullOrWhiteSpace(receipt.ReviewReference)
                || receipt.ReviewReference.Length > 256
                || receipt.ReviewReference != receipt.ReviewReference.Trim()
                || receipt.RecordedAt < receipt.Allocation.Execution.ObservedAt
                || receipt.ReviewExpiresAt is { } expiresAt && expiresAt <= receipt.RecordedAt
            )
        )
        {
            throw new InvalidOperationException(
                "Manual-sale audit journal is invalid; retained evidence was not reset."
            );
        }

        if (this.Receipts.Count > 0)
        {
            var allocations = this.Receipts.Select(receipt => receipt.Allocation).ToArray();
            var first = allocations[0];
            _ = ManualSaleAllocationPlanner.Allocate(first.Lot, first.Execution, first.Quantity, allocations);
            if (first.Lot.AccountKey != accountKey)
            {
                throw new InvalidOperationException("Manual-sale audit journal belongs to another account.");
            }
        }
    }
}
