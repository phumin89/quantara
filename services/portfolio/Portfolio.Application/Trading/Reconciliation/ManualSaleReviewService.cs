using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

public sealed class ManualSaleReviewService(
    IAccountContext account,
    IReadOnlyTradingStore trading,
    IManualSaleAllocationStore allocations,
    IManualSaleEvidenceReader broker,
    ManualSaleReconciliation reconciliation,
    ManualSaleReviewProof proofs,
    TimeProvider time,
    SemaphoreSlim gate
) : IManualSaleReview
{
    public const string Acknowledgement =
        "Retire verified empty strategy inventory; retain original lots and unknown profit. Credit only evidenced net THB. Do not reset capital or enable trading.";

    public async Task<ManualSaleReviewResult> Preview(
        string symbol,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        var state = await trading.Read(cancellationToken);
        if (state.Mode != TradingMode.Stopped || state.Live.Orders.Any(TradingLedgerAccounting.Unresolved))
        {
            throw new InvalidOperationException(
                "Stop trading and resolve active engine orders before reviewing attribution."
            );
        }

        var evidence = await broker.Read(symbol, executionId, cancellationToken);
        this.ValidateEvidence(evidence);
        if (evidence.Execution.Symbol != symbol || evidence.Execution.ExecutionId != executionId)
        {
            throw new InvalidOperationException("Broker evidence does not match the requested execution.");
        }

        if (
            state.Mandate is { RevokedAt: null } mandate && mandate.ExpiresAt > time.GetUtcNow()
            || state.Live.Fills.Any(fill => fill.Symbol == symbol && fill.BrokerFillId == executionId)
            || state.Live.Orders.Any(order =>
                order.Symbol == symbol && order.BrokerOrderId == evidence.Execution.OrderId
            )
        )
        {
            throw new InvalidOperationException("The sale is engine-owned or live authority is still valid.");
        }

        var lot = ManualSaleReconciliation.OwnedLot(account.AccountKey, state, symbol);
        var journal = await allocations.Read(cancellationToken);
        journal.Validate(account.AccountKey);
        var previous = journal.Receipts.SingleOrDefault(receipt =>
            receipt.Allocation.Lot.LotKey == lot.LotKey && receipt.Allocation.Execution.ExecutionId == executionId
        );
        var execution = previous?.Allocation.Execution ?? evidence.Execution;
        if (!SameExecution(execution, evidence.Execution))
        {
            throw new InvalidOperationException("Broker evidence conflicts with retained attribution.");
        }

        var plan = ManualSaleAllocationPlanner.Allocate(
            lot,
            execution,
            execution.Quantity,
            journal.Receipts.Select(receipt => receipt.Allocation).ToArray()
        );
        var now = time.GetUtcNow();
        var snapshot = new ManualSaleReviewSnapshot(
            lot,
            execution,
            execution.Quantity,
            evidence.Balance,
            evidence.ObservedAt,
            state.Revision,
            MaterialBinding(state, lot),
            journal.Revision,
            now,
            now.AddMinutes(5),
            evidence.Settlement,
            evidence.ProceedsEstimate
        );
        return Result(snapshot, plan, proofs.Protect(snapshot), false);
    }

    public async Task<ManualSaleReviewResult> Apply(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    )
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await this.ApplyReviewed(proof, acknowledgement, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ManualSaleReviewResult> ApplyReviewed(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    )
    {
        if (acknowledgement != Acknowledgement)
        {
            throw new ArgumentException("Review the exact attribution and acknowledge its accounting limitations.");
        }

        var snapshot = proofs.Read(proof);
        if (snapshot.Lot.AccountKey != account.AccountKey || snapshot.Execution.AccountKey != account.AccountKey)
        {
            throw new ArgumentException("Manual-sale review belongs to another account.");
        }

        var reviewReference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
        var journal = await allocations.Read(cancellationToken);
        journal.Validate(account.AccountKey);
        var state = await trading.Read(cancellationToken);
        var retired = state.Live.ExternalSales.SingleOrDefault(sale => sale.ReviewReference == reviewReference);
        if (retired is not null)
        {
            if (
                retired.Allocation.Lot != snapshot.Lot
                || retired.Allocation.Execution != snapshot.Execution
                || retired.Settlement != snapshot.Settlement
                || retired.ProceedsEstimate != snapshot.ProceedsEstimate
                || !journal.Receipts.Any(receipt => receipt.Allocation == retired.Allocation)
            )
            {
                throw new InvalidOperationException(
                    "Retained inventory reconciliation conflicts with the review evidence."
                );
            }

            var retainedPlan = ManualSaleAllocationPlanner.Allocate(
                snapshot.Lot,
                snapshot.Execution,
                snapshot.Quantity,
                journal.Receipts.Select(receipt => receipt.Allocation).ToArray()
            );
            return Result(snapshot, retainedPlan, proof, true);
        }

        var now = time.GetUtcNow();
        if (
            snapshot.IssuedAt > now
            || now >= snapshot.ExpiresAt
            || snapshot.ExpiresAt - snapshot.IssuedAt != TimeSpan.FromMinutes(5)
            || journal.Revision != snapshot.JournalRevision
        )
        {
            throw new InvalidOperationException("Manual-sale review expired or changed; request a new preview.");
        }

        if (
            MaterialBinding(state, ManualSaleReconciliation.OwnedLot(account.AccountKey, state, snapshot.Lot.Symbol))
            != snapshot.TradingBinding
        )
        {
            throw new InvalidOperationException("The reviewed strategy state changed; no attribution was applied.");
        }

        var fresh = await broker.Read(snapshot.Execution.Symbol, snapshot.Execution.ExecutionId, cancellationToken);
        this.ValidateEvidence(fresh);
        if (
            !SameExecution(snapshot.Execution, fresh.Execution)
            || fresh.Balance != snapshot.Balance
            || fresh.Settlement != snapshot.Settlement
            || fresh.ProceedsEstimate != snapshot.ProceedsEstimate
        )
        {
            throw new InvalidOperationException("Broker execution or wallet evidence changed; request a new preview.");
        }

        if (time.GetUtcNow() >= snapshot.ExpiresAt)
        {
            throw new InvalidOperationException(
                "The review expired during broker verification; no attribution was applied."
            );
        }

        var current = await trading.Read(cancellationToken);
        if (
            MaterialBinding(
                current,
                ManualSaleReconciliation.OwnedLot(account.AccountKey, current, snapshot.Lot.Symbol)
            ) != snapshot.TradingBinding
        )
        {
            throw new InvalidOperationException("The reviewed material state changed during broker verification.");
        }

        var plan = await reconciliation.RecordReviewed(
            snapshot.Execution,
            snapshot.Quantity,
            reviewReference,
            current.Revision,
            snapshot.JournalRevision,
            snapshot.ExpiresAt,
            fresh,
            cancellationToken
        );
        return Result(snapshot, plan, proof, true);
    }

    private void ValidateEvidence(ManualSaleBrokerEvidence evidence)
    {
        var now = time.GetUtcNow();
        if (
            evidence.Execution.AccountKey != account.AccountKey
            || evidence.ObservedAt > now
            || now - evidence.ObservedAt > TimeSpan.FromSeconds(30)
            || evidence.Execution.ObservedAt != evidence.ObservedAt
            || evidence.Balance.Symbol != evidence.Execution.Symbol
            || evidence.Balance.Available is null or < 0
            || evidence.Balance.Quantity < evidence.Balance.Available
            || evidence.Balance.Quantity != 0m
            || evidence.Balance.Available != 0m
        )
        {
            throw new InvalidOperationException("Fresh, complete account-matched broker evidence is required.");
        }
    }

    private static bool SameExecution(ManualSaleExecution first, ManualSaleExecution second) =>
        first with { ObservedAt = second.ObservedAt } == second;

    private static string MaterialBinding(TradingState state, ManualSaleLot lot) =>
        Convert
            .ToHexString(
                SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            lot,
                            state.Mode,
                            state.RulesHash,
                            state.EmergencyStopped,
                            state.Mandate,
                            state.Live.FundingThb,
                            state.Live.CashThb,
                        }
                    )
                )
            )
            .ToLowerInvariant();

    private static ManualSaleReviewResult Result(
        ManualSaleReviewSnapshot snapshot,
        ManualSaleAllocationPlan plan,
        string proof,
        bool applied
    ) =>
        new(
            snapshot.Lot.Symbol,
            plan.AllocatedSoldQuantity,
            plan.UnallocatedHistoricalQuantity,
            plan.AllocatedCostThb,
            plan.RealizedNetProfitThb,
            snapshot.Balance,
            snapshot.BrokerObservedAt,
            snapshot.ExpiresAt,
            proof,
            Acknowledgement,
            applied,
            snapshot.Settlement is null,
            snapshot.ProceedsEstimate
        );
}
