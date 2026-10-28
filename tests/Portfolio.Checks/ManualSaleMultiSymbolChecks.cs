using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class ManualSaleMultiSymbolChecks
{
    public static async Task Run()
    {
        var openedAt = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var soldAt = openedAt.AddHours(1);
        var now = soldAt.AddMinutes(2);
        var account = new string('a', 64);
        var initial = TradingStateFactory.Create(openedAt);
        var firstHolding = new TradingHolding("AAA", 10m, 1000m, openedAt, 100m);
        var secondHolding = new TradingHolding("BBB", 20m, 2000m, openedAt, 100m);
        var state = initial with
        {
            Mode = TradingMode.Stopped,
            Live = initial.Live with
            {
                FundingThb = 5000m,
                CashThb = 2000m,
                Positions = [firstHolding, secondHolding],
                Equity = [new TradingEquityMark(openedAt, 5000m, 5000m)],
            },
        };
        var firstExecution = new ManualSaleExecution(
            account,
            "synthetic-shared-execution",
            "synthetic-shared-order",
            "AAA",
            TradeSide.Sell,
            firstHolding.Quantity,
            soldAt,
            now,
            "synthetic-reviewed-AAA-evidence"
        );
        var secondExecution = firstExecution with
        {
            Symbol = "BBB",
            Quantity = secondHolding.Quantity,
            EvidenceReference = "synthetic-reviewed-BBB-evidence",
        };
        var imported = new PortfolioLedger
        {
            UpdatedAt = now,
            HistoryUpdatedAt = now,
            Balances = [new Balance("AAA", 0m, 0m), new Balance("BBB", 0m, 0m)],
            Trades =
            [
                new Trade(
                    firstExecution.ExecutionId,
                    "AAA",
                    TradeSide.Sell,
                    firstExecution.Quantity,
                    110m,
                    1m,
                    0m,
                    soldAt
                ),
                new Trade(
                    secondExecution.ExecutionId,
                    "BBB",
                    TradeSide.Sell,
                    secondExecution.Quantity,
                    120m,
                    2m,
                    0m,
                    soldAt
                ),
            ],
        };
        var trading = new FakeTradingStore(state);
        var store = new FakeManualSaleAllocationStore(account, trading);
        var clock = new OrderCheckTime { Now = now };
        var reconciliation = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported),
            store,
            clock
        );
        var firstEvidence = new ManualSaleBrokerEvidence(firstExecution, imported.Balances[0], now);
        var broker = new FakeManualSaleEvidenceReader(firstEvidence);
        var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-multi-symbol-owner-control"));
        using var gate = new SemaphoreSlim(1, 1);
        var workflow = new ManualSaleReviewService(
            new AccountContext(account),
            trading,
            store,
            broker,
            reconciliation,
            proofs,
            clock,
            gate
        );
        var before = JsonSerializer.Serialize(state);
        await Reject(
            () => workflow.Preview("BBB", firstExecution.ExecutionId, CancellationToken.None),
            "Requested symbol must match separately verified evidence"
        );
        Check(
            store.AppendCalls == 0 && JsonSerializer.Serialize(trading.State) == before,
            "Mismatched requested symbol writes no journal or inventory"
        );

        var firstPreview = await workflow.Preview("AAA", firstExecution.ExecutionId, CancellationToken.None);
        Check(store.AppendCalls == 0 && !firstPreview.Applied, "First preview remains read-only");
        var firstApplied = await workflow.Apply(
            firstPreview.Proof,
            firstPreview.Acknowledgement,
            CancellationToken.None
        );
        Check(
            firstApplied.Applied
                && store.Journal.Revision == 1
                && trading.State.Live.Positions.Single() == secondHolding
                && trading.State.Live.ExternalSales.Single().OriginalPosition == firstHolding,
            "First reviewed symbol retires without changing the other lot"
        );

        clock.Now = now.AddSeconds(1);
        secondExecution = secondExecution with { ObservedAt = clock.Now };
        broker.Evidence = new ManualSaleBrokerEvidence(secondExecution, imported.Balances[1], clock.Now);
        var secondPreview = await workflow.Preview("BBB", secondExecution.ExecutionId, CancellationToken.None);
        var secondSnapshot = proofs.Read(secondPreview.Proof);
        Check(
            secondSnapshot.JournalRevision == 1 && secondSnapshot.TradingRevision == trading.State.Revision,
            "Second preview binds the revisions after the first retirement"
        );
        var secondApplied = await workflow.Apply(
            secondPreview.Proof,
            secondPreview.Acknowledgement,
            CancellationToken.None
        );
        Check(
            secondApplied.Applied
                && store.AppendCalls == 2
                && store.Journal.Revision == 2
                && trading.State.Revision == state.Revision + 2
                && trading.State.Live.Positions.Count == 0,
            "Separately reviewed symbols sharing a raw execution ID both retire exactly once"
        );
        Check(
            store.Journal.Receipts.Count == 2
                && store.Journal.Receipts.Select(receipt => receipt.Allocation.Execution.ExecutionId).Distinct().Count()
                    == 1
                && store
                    .Journal.Receipts.Select(receipt => receipt.Allocation.Execution.Symbol)
                    .ToHashSet()
                    .SetEquals(["AAA", "BBB"])
                && trading.State.Live.ExternalSales.Count == 2
                && trading
                    .State.Live.ExternalSales.Select(sale => sale.Allocation.Execution.Symbol)
                    .ToHashSet()
                    .SetEquals(["AAA", "BBB"]),
            "Both symbol-scoped receipts and external sales survive the fake store JSON roundtrip"
        );
        foreach (var holding in new[] { firstHolding, secondHolding })
        {
            var receipt = store.Journal.Receipts.Single(item => item.Allocation.Execution.Symbol == holding.Symbol);
            var sale = trading.State.Live.ExternalSales.Single(item => item.OriginalPosition.Symbol == holding.Symbol);
            Check(
                receipt.Allocation == sale.Allocation
                    && sale.OriginalPosition == holding
                    && receipt.Allocation.Quantity == holding.Quantity
                    && receipt.Allocation.Lot.RecordedCostThb == holding.CostThb
                    && sale.Wallet.Quantity == 0m
                    && sale.Wallet.Available == 0m
                    && sale.Settlement is null,
                "Each persisted sale retains its own lot, quantity, recorded cost and empty-wallet evidence"
            );
        }

        Check(
            firstApplied.AllocatedCostThb is null
                && firstApplied.RealizedNetProfitThb is null
                && firstApplied.CashSettlementPending
                && secondApplied.AllocatedCostThb is null
                && secondApplied.RealizedNetProfitThb is null
                && secondApplied.CashSettlementPending
                && TradingLedgerAccounting.Equity(trading.State.Live, [], clock.Now, 90) is null,
            "Two external sales do not invent allocated cost, profit or cash settlement"
        );
        Check(
            trading.State.Live.CashThb == state.Live.CashThb
                && trading.State.Live.FundingThb == state.Live.FundingThb
                && trading.State.Mode == state.Mode
                && trading.State.Mandate == state.Mandate
                && trading.State.EmergencyStopped == state.EmergencyStopped
                && trading.State.RulesHash == state.RulesHash
                && trading.State.Live.Fills.Count == state.Live.Fills.Count
                && trading.State.Live.Orders.Count == state.Live.Orders.Count
                && trading.State.Live.CashBaselines.Count == state.Live.CashBaselines.Count,
            "Retirements preserve cash, funding, authority and original engine activity"
        );

        var persistedState = JsonSerializer.Serialize(trading.State);
        var persistedJournal = JsonSerializer.Serialize(store.Journal);
        var reads = broker.Reads;
        var historyCount = trading.History.Count;
        clock.Now = secondPreview.ExpiresAt.AddSeconds(1);
        var restarted = new ManualSaleReviewService(
            new AccountContext(account),
            trading,
            store,
            broker,
            reconciliation,
            proofs,
            clock,
            gate
        );
        var replayed = await restarted.Apply(firstPreview.Proof, firstPreview.Acknowledgement, CancellationToken.None);
        Check(
            JsonSerializer.Serialize(replayed) == JsonSerializer.Serialize(firstApplied)
                && broker.Reads == reads
                && store.AppendCalls == 2
                && trading.History.Count == historyCount
                && JsonSerializer.Serialize(trading.State) == persistedState
                && JsonSerializer.Serialize(store.Journal) == persistedJournal,
            "Expired first proof returns its retained result after the second commit without broker reread, append or duplicate credit"
        );
    }

    private static async Task Reject<T>(Func<Task<T>> action, string message)
    {
        try
        {
            _ = await action();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Expected multi-symbol manual-sale rejection: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
