using System.Globalization;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

// Unreviewed recording remains audit-only. Reviewed retirement atomically preserves evidence and removes empty inventory.
public sealed class ManualSaleReconciliation(
    string accountKey,
    IReadOnlyTradingStore trading,
    IReadOnlyPortfolioRepository portfolio,
    IManualSaleAllocationStore allocations,
    TimeProvider time
)
{
    public Task<ManualSaleAllocationPlan> Record(
        ManualSaleExecution execution,
        decimal quantity,
        string reviewReference,
        CancellationToken cancellationToken
    ) => this.RecordCore(execution, quantity, reviewReference, null, null, null, null, cancellationToken);

    public Task<ManualSaleAllocationPlan> RecordReviewed(
        ManualSaleExecution execution,
        decimal quantity,
        string reviewReference,
        long expectedTradingRevision,
        long expectedJournalRevision,
        DateTimeOffset reviewExpiresAt,
        ManualSaleBrokerEvidence verified,
        CancellationToken cancellationToken
    ) =>
        this.RecordCore(
            execution,
            quantity,
            reviewReference,
            expectedTradingRevision,
            expectedJournalRevision,
            reviewExpiresAt,
            verified,
            cancellationToken
        );

    private async Task<ManualSaleAllocationPlan> RecordCore(
        ManualSaleExecution execution,
        decimal quantity,
        string reviewReference,
        long? expectedTradingRevision,
        long? expectedJournalRevision,
        DateTimeOffset? reviewExpiresAt,
        ManualSaleBrokerEvidence? verified,
        CancellationToken cancellationToken
    )
    {
        var state = await trading.Read(cancellationToken);
        var now = time.GetUtcNow();
        if (
            state.Mode != TradingMode.Stopped
            || expectedTradingRevision is { } tradingRevision && state.Revision != tradingRevision
            || state.Mandate is { RevokedAt: null } mandate && mandate.ExpiresAt > now
            || state.Live.Orders.Any(TradingLedgerAccounting.Unresolved)
            || execution.AccountKey != accountKey
            || execution.ObservedAt > now
            || reviewExpiresAt is { } expiresAt && now >= expiresAt
            || state.Live.Fills.Any(fill =>
                fill.Symbol == execution.Symbol && fill.BrokerFillId == execution.ExecutionId
            )
            || state.Live.Orders.Any(order =>
                order.Symbol == execution.Symbol && order.BrokerOrderId == execution.OrderId
            )
        )
        {
            throw new InvalidOperationException(
                "Manual-sale attribution requires stopped authority and no active engine orders."
            );
        }

        var evidence = await portfolio.Load(cancellationToken);
        var imported = evidence.Trades.SingleOrDefault(trade =>
            trade.Symbol == execution.Symbol && trade.TransactionId == execution.ExecutionId
        );
        if (
            imported is null
            || imported.Side != TradeSide.Sell
            || imported.Quantity != execution.Quantity
            || imported.Timestamp != execution.OccurredAt
            || evidence.HistoryUpdatedAt is null
            || evidence.HistoryUpdatedAt < execution.OccurredAt
            || evidence.HistoryUpdatedAt > now
        )
        {
            throw new InvalidOperationException(
                "Manual-sale evidence does not match the retained account trade history."
            );
        }

        var lot = OwnedLot(accountKey, state, execution.Symbol);
        var journal = await allocations.Read(cancellationToken);
        journal.Validate(accountKey);
        if (expectedJournalRevision is { } journalRevision && journal.Revision != journalRevision)
        {
            throw new InvalidOperationException(
                "The reviewed attribution journal changed; no automatic retry was performed."
            );
        }

        var existing = journal.Receipts.Select(receipt => receipt.Allocation).ToArray();
        var plan = ManualSaleAllocationPlanner.Allocate(lot, execution, quantity, existing);
        if (plan.Journal.Count == existing.Length && verified is null)
        {
            return plan;
        }

        var allocation = plan.Journal.Single(item =>
            item.Lot.LotKey == lot.LotKey && item.Execution.ExecutionId == execution.ExecutionId
        );
        var receipt = new ManualSaleAllocationReceipt(allocation, reviewReference, now, reviewExpiresAt);
        TradingExternalSale? retirement = null;
        if (verified is not null)
        {
            if (
                verified.Execution with { ObservedAt = execution.ObservedAt } != execution
                || verified.ObservedAt != verified.Execution.ObservedAt
            )
            {
                throw new InvalidOperationException("Fresh broker evidence does not match the reviewed sale.");
            }

            retirement = new TradingExternalSale(
                state.Live.Positions.Single(position => position.Symbol == execution.Symbol),
                allocation,
                verified.Balance,
                verified.ObservedAt,
                now,
                reviewReference,
                verified.Settlement,
                verified.ProceedsEstimate
            );
            _ = TradingManualSaleInventory.Retire(state, retirement, accountKey, now);
        }

        var isNew = plan.Journal.Count != existing.Length;
        var proposed = journal with
        {
            Revision = journal.Revision + (isNew ? 1 : 0),
            Receipts = isNew ? journal.Receipts.Append(receipt).ToArray() : journal.Receipts,
        };
        proposed.Validate(accountKey);
        if (!await allocations.Append(receipt, journal.Revision, state.Revision, cancellationToken, retirement))
        {
            throw new InvalidOperationException(
                "Trading state or manual-sale audit changed; no automatic retry was performed."
            );
        }

        return plan;
    }

    internal static ManualSaleLot OwnedLot(string accountKey, TradingState state, string symbol)
    {
        var holding =
            state.Live.Positions.SingleOrDefault(position => position.Symbol == symbol)
            ?? throw new InvalidOperationException("The explicitly attributed strategy lot is missing.");
        return new ManualSaleLot(
            accountKey,
            holding.Symbol + ":" + holding.OpenedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            holding.Symbol,
            holding.OpenedAt,
            holding.Quantity,
            holding.CostThb
        );
    }
}
