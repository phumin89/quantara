namespace Quantara.Portfolio.Domain;

// Builds a sidecar attribution journal. It cannot change holdings, fills, cash, cost basis or execution authority.
public static class ManualSaleAllocationPlanner
{
    public static ManualSaleAllocationPlan Allocate(
        ManualSaleLot lot,
        ManualSaleExecution execution,
        decimal quantity,
        IReadOnlyList<ManualSaleAllocation> journal
    )
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.Count > 1024)
        {
            throw new InvalidOperationException("Manual-sale journal exceeds the review bound.");
        }

        var requested = new ManualSaleAllocation(lot, execution, quantity);
        Validate(requested);
        foreach (var allocation in journal)
        {
            Validate(allocation);
            if (allocation.Lot.AccountKey != lot.AccountKey)
            {
                throw new InvalidOperationException("Manual-sale journal crosses account boundaries.");
            }
        }

        var previous = journal.SingleOrDefault(allocation =>
            allocation.Lot.LotKey == lot.LotKey && allocation.Execution.ExecutionId == execution.ExecutionId
        );
        if (previous is not null && previous != requested)
        {
            throw new InvalidOperationException("Conflicting manual-sale allocation identity.");
        }

        if (previous is null && journal.Count == 1024)
        {
            throw new InvalidOperationException("Manual-sale journal exceeds the review bound.");
        }

        var proposed = previous is null ? journal.Append(requested).ToArray() : journal.ToArray();
        foreach (var lotGroup in proposed.GroupBy(allocation => allocation.Lot.LotKey, StringComparer.Ordinal))
        {
            var original = lotGroup.First().Lot;
            if (
                lotGroup.Any(allocation => allocation.Lot != original)
                || lotGroup
                    .Select(allocation => allocation.Execution.ExecutionId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != lotGroup.Count()
                || lotGroup.Sum(allocation => allocation.Quantity)
                    > Math.Min(original.RecordedQuantity, original.BrokerCreditedQuantity ?? original.RecordedQuantity)
            )
            {
                throw new InvalidOperationException("Manual-sale allocation exceeds or conflicts with the owned lot.");
            }
        }

        // Imported history and broker verification scope each transaction identity to its symbol.
        foreach (
            var executionGroup in proposed.GroupBy(allocation =>
                (allocation.Execution.Symbol, allocation.Execution.ExecutionId)
            )
        )
        {
            var receipt = executionGroup.First().Execution;
            if (
                executionGroup.Any(allocation => allocation.Execution != receipt)
                || executionGroup.Sum(allocation => allocation.Quantity) > receipt.Quantity
            )
            {
                throw new InvalidOperationException(
                    "Broker execution was already allocated or has conflicting evidence."
                );
            }
        }

        var sold = proposed
            .Where(allocation => allocation.Lot.LotKey == lot.LotKey)
            .Sum(allocation => allocation.Quantity);
        return new ManualSaleAllocationPlan(
            lot,
            Array.AsReadOnly(proposed),
            sold,
            lot.RecordedQuantity - sold,
            lot.BrokerCreditedQuantity is { } credited ? credited - sold : null,
            lot.RecordedCostThb,
            null,
            null
        );
    }

    private static void Validate(ManualSaleAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        var lot = allocation.Lot;
        var execution = allocation.Execution;
        if (
            lot is null
            || execution is null
            || !Identity(lot.AccountKey)
            || !Identity(lot.LotKey)
            || !Identity(lot.Symbol)
            || !Identity(execution.ExecutionId)
            || !Identity(execution.OrderId)
            || !Identity(execution.EvidenceReference)
            || execution.AccountKey != lot.AccountKey
            || execution.Symbol != lot.Symbol
            || execution.Side != TradeSide.Sell
            || lot.RecordedQuantity <= 0
            || lot.RecordedCostThb < 0
            || lot.BrokerCreditedQuantity is < 0
            || execution.Quantity <= 0
            || allocation.Quantity <= 0
            || allocation.Quantity > execution.Quantity
            || execution.OccurredAt < lot.OpenedAt
            || execution.ObservedAt < execution.OccurredAt
        )
        {
            throw new InvalidOperationException("Manual-sale evidence or explicit lot attribution is invalid.");
        }
    }

    private static bool Identity(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value == value.Trim();
}
