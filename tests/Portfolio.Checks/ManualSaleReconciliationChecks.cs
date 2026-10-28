using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class ManualSaleReconciliationChecks
{
    public static async Task Run()
    {
        ManualSaleIdentityChecks.Run();
        await ManualSaleMultiSymbolChecks.Run();
        var opened = new DateTimeOffset(2026, 10, 20, 12, 31, 53, 438, TimeSpan.Zero);
        var soldAt = new DateTimeOffset(2026, 10, 20, 15, 29, 24, 936, TimeSpan.Zero);
        var now = soldAt.AddMinutes(2);
        var account = new string('a', 64);
        var initial = TradingStateFactory.Create(opened);
        var state = initial with
        {
            Mode = TradingMode.Stopped,
            Live = initial.Live with
            {
                FundingThb = 5000m,
                CashThb = 49.82m,
                Equity = [new TradingEquityMark(opened, 5000m, 5000m)],
                Positions = [new TradingHolding("WLD", 268.30338665m, 4950.18m, opened, 18.45m)],
            },
        };
        var execution = new ManualSaleExecution(
            account,
            "confirmed-sale",
            "manual-order",
            "WLD",
            TradeSide.Sell,
            268.30336763m,
            soldAt,
            soldAt.AddMinutes(1),
            "retained-history-and-reviewed-order-info"
        );
        var imported = new PortfolioLedger
        {
            HistoryUpdatedAt = now,
            UpdatedAt = now,
            Balances = [new Balance("WLD", 0m)],
            Trades =
            [
                new Trade(
                    execution.ExecutionId,
                    "WLD",
                    TradeSide.Sell,
                    execution.Quantity,
                    18.85m,
                    12.65m,
                    12.65m,
                    soldAt
                ),
            ],
        };
        var trading = new FakeTradingStore(state);
        var store = new FakeManualSaleAllocationStore(account, trading);
        var service = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported),
            store,
            new FixedTimeProvider(now)
        );
        var before = JsonSerializer.Serialize(state);
        var plan = await service.Record(execution, execution.Quantity, "owner-review-fixture", CancellationToken.None);
        Check(
            plan.AllocatedSoldQuantity == 268.30336763m && plan.UnallocatedHistoricalQuantity == .00001902m,
            "Exact verified sale and original residue"
        );
        Check(
            plan.AllocatedCostThb is null
                && plan.RealizedNetProfitThb is null
                && plan.UnresolvedRecordedCostThb == 4950.18m,
            "Unknown allocation cost and profit stay unknown"
        );
        Check(
            JsonSerializer.Serialize(trading.State) == before && trading.History.Count == 1,
            "Audit-only recording does not change ledger or permissions"
        );
        Check(
            imported.Balances.Single().Quantity == 0m && store.Journal.Revision == 1,
            "Wallet zero is separate truth; exactly one audit revision"
        );
        var restarted = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported),
            store,
            new FixedTimeProvider(now)
        );
        _ = await restarted.Record(execution, execution.Quantity, "repeat-review-fixture", CancellationToken.None);
        Check(
            store.AppendCalls == 1 && store.Journal.Receipts.Single().ReviewReference == "owner-review-fixture",
            "Restarted duplicate keeps original audit and never appends twice"
        );

        await Reject(() => service.Record(execution, 1m, "conflict", CancellationToken.None), "Conflicting allocation");
        await Reject(
            () =>
                service.Record(
                    execution with
                    {
                        AccountKey = new string('b', 64),
                    },
                    1m,
                    "cross-account",
                    CancellationToken.None
                ),
            "Account isolation"
        );
        trading.State = state with { Mode = TradingMode.AutoLive };
        await Reject(
            () => service.Record(execution, execution.Quantity, "active", CancellationToken.None),
            "No live authority during attribution"
        );
        trading.State = state with
        {
            Live = state.Live with
            {
                Fills =
                [
                    new TradingFill(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        execution.ExecutionId,
                        "WLD",
                        TradeSide.Sell,
                        1m,
                        18.85m,
                        18.85m,
                        0m,
                        soldAt
                    ),
                ],
            },
        };
        await Reject(
            () => service.Record(execution, execution.Quantity, "engine-fill", CancellationToken.None),
            "Never attribute an engine-owned execution again"
        );
        trading.State = state with
        {
            Live = state.Live with
            {
                Orders =
                [
                    new TradingIntent(
                        Guid.NewGuid(),
                        "WLD",
                        TradeSide.Sell,
                        1m,
                        18.85m,
                        0m,
                        opened,
                        0L,
                        state.RulesHash,
                        "fixture",
                        "filled",
                        execution.OrderId,
                        LastInspectedAt: soldAt
                    ),
                ],
            },
        };
        await Reject(
            () => service.Record(execution, execution.Quantity, "engine-order", CancellationToken.None),
            "Retained engine order ownership is independent of journal dedup"
        );
        trading.State = state;
        var emptyHistory = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported with { Trades = [] }),
            store,
            new FixedTimeProvider(now)
        );
        await Reject(
            () => emptyHistory.Record(execution, execution.Quantity, "missing", CancellationToken.None),
            "Cannot invent missing execution evidence"
        );
        var noHistoryTimestamp = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported with { HistoryUpdatedAt = null }),
            store,
            new FixedTimeProvider(now)
        );
        await Reject(
            () => noHistoryTimestamp.Record(execution, execution.Quantity, "unknown", CancellationToken.None),
            "Missing provenance is not current history"
        );

        foreach (var afterCommit in new[] { false, true })
        {
            var failureStore = new FakeManualSaleAllocationStore(account, trading)
            {
                FailBeforeCommit = !afterCommit,
                FailAfterCommit = afterCommit,
            };
            var failureService = new ManualSaleReconciliation(
                account,
                trading,
                new PortfolioTestRepository(imported),
                failureStore,
                new FixedTimeProvider(now)
            );
            try
            {
                _ = await failureService.Record(execution, execution.Quantity, "save-failure", CancellationToken.None);
                throw new InvalidOperationException("Expected synthetic save failure.");
            }
            catch (IOException) { }

            Check(
                failureStore.AppendCalls == 1 && failureStore.Journal.Revision == (afterCommit ? 1 : 0),
                "No automatic save replay after ambiguous acknowledgement"
            );
            failureStore.FailBeforeCommit = false;
            failureStore.FailAfterCommit = false;
            _ = await failureService.Record(execution, execution.Quantity, "owner-retry", CancellationToken.None);
            Check(
                failureStore.Journal.Receipts.Count == 1 && failureStore.AppendCalls == (afterCommit ? 1 : 2),
                "Read persisted evidence before an explicitly repeated request"
            );
        }

        var lostStore = new FakeManualSaleAllocationStore(account, trading) { LoseClaim = true };
        var lost = new ManualSaleReconciliation(
            account,
            trading,
            new PortfolioTestRepository(imported),
            lostStore,
            new FixedTimeProvider(now)
        );
        await Reject(
            () => lost.Record(execution, execution.Quantity, "lost-cas", CancellationToken.None),
            "Losing journal CAS never retries"
        );
        Check(lostStore.AppendCalls == 1 && lostStore.Journal.Revision == 0, "CAS loss leaves audit unchanged");
        await OwnerReview(state, imported, execution, account, now);
        Console.WriteLine(
            "Manual-sale reconciliation mocks passed: evidence/ownership, audit CAS, restart/dedup and ambiguous-save recovery; no broker or SQL calls."
        );
    }

    private static async Task OwnerReview(
        TradingState state,
        PortfolioLedger imported,
        ManualSaleExecution execution,
        string account,
        DateTimeOffset now
    )
    {
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
        var evidence = new ManualSaleBrokerEvidence(
            execution with
            {
                ObservedAt = now,
            },
            new Balance("WLD", 0m, 0m),
            now,
            ProceedsEstimate: BitkubManualSaleEvidenceReader.EstimateProceeds(imported.Trades.Single())
        );
        var broker = new FakeManualSaleEvidenceReader(evidence);
        using var orderEvidence = JsonSerializer.SerializeToDocument(
            new
            {
                id = execution.OrderId,
                status = "filled",
                remaining = 0m,
                filled = execution.Quantity,
                total = execution.Quantity,
                client_id = "",
                history = new[]
                {
                    new
                    {
                        txn_id = execution.ExecutionId,
                        amount = execution.Quantity,
                        rate = 18.85m,
                        fee = 12.65m,
                        credit = 12.65m,
                        timestamp = execution.OccurredAt.ToUnixTimeMilliseconds(),
                    },
                },
            }
        );
        Check(
            BitkubManualSaleEvidenceReader
                .VerifyOrder(orderEvidence.RootElement, imported.Trades.Single(), execution.OrderId)
                .StartsWith("bitkub-sale-v1:", StringComparison.Ordinal),
            "Server derives stable evidence from matching history and completed order"
        );
        try
        {
            _ = BitkubManualSaleEvidenceReader.VerifyOrder(
                orderEvidence.RootElement,
                imported.Trades.Single(),
                "wrong-order"
            );
            throw new InvalidOperationException("Mismatched broker order evidence was accepted.");
        }
        catch (InvalidDataException) { }

        var proofs = new ManualSaleReviewProof(new TradingControlOptions("synthetic-existing-owner-control"));
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
        var preview = await workflow.Preview("WLD", execution.ExecutionId, CancellationToken.None);
        Check(
            store.AppendCalls == 0 && !preview.Applied && preview.BrokerBalance.Quantity == 0m,
            "Preview verifies source truth without writing an audit or treating residual as wallet holdings"
        );
        Check(
            preview.AllocatedCostThb is null && preview.RealizedNetProfitThb is null,
            "Review never fabricates cost or profit"
        );
        Check(
            preview.ProceedsEstimate is { } estimate
                && estimate.GrossThb == execution.Quantity * 18.85m
                && estimate.NetFeesThb == 0m
                && estimate.UnroundedNetThb == estimate.GrossThb
                && preview.CashSettlementPending,
            "Reported fill arithmetic stays an explicitly unrounded estimate, not cash settlement"
        );
        await Reject(
            () => workflow.Apply(preview.Proof, "unconfirmed", CancellationToken.None),
            "Exact owner acknowledgement is required"
        );
        var changedProof = preview.Proof[..^1] + (preview.Proof[^1] == '0' ? "1" : "0");
        await Reject(
            () => workflow.Apply(changedProof, preview.Acknowledgement, CancellationToken.None),
            "Tampered proof cannot authorize attribution"
        );
        trading.State = state with { Revision = state.Revision + 1, EmergencyStopped = true };
        await Reject(
            () => workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None),
            "Owner review binds material authority state"
        );
        trading.State = state;
        broker.Evidence = evidence with
        {
            Execution = evidence.Execution with { EvidenceReference = "changed-broker-evidence" },
        };
        await Reject(
            () => workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None),
            "Broker changes invalidate consent"
        );
        broker.Evidence = evidence;
        clock.Now = now.AddMinutes(5);
        await Reject(
            () => workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None),
            "Expired owner review does not write"
        );
        clock.Now = now;
        Check(store.AppendCalls == 0, "Every invalid review left the durable journal unchanged");
        var maintained = state with { Revision = state.Revision + 1, HeartbeatAt = now };
        trading.State = maintained;
        broker.BeforeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applying = workflow.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
        var competingWrite = gate.WaitAsync();
        Check(!competingWrite.IsCompleted, "Inventory confirmation excludes concurrent engine writes");
        broker.BeforeRead.SetResult();
        var applied = await applying;
        await competingWrite;
        gate.Release();
        broker.BeforeRead = null;
        Check(
            applied.Applied
                && store.AppendCalls == 1
                && store.Journal.Receipts.Single().ReviewExpiresAt == preview.ExpiresAt,
            "Heartbeat-only revision changes preserve consent while current revision CAS protects the append"
        );
        var reads = broker.Reads;
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
        _ = await restarted.Apply(preview.Proof, preview.Acknowledgement, CancellationToken.None);
        Check(
            store.AppendCalls == 1 && broker.Reads == reads,
            "Restart replay returns the durable result without repeated action"
        );
        Check(
            trading.State.Live.Positions.Count == 0
                && trading.State.Live.ExternalSales.Single().OriginalPosition == maintained.Live.Positions.Single()
                && trading.State.Live.ExternalSales.Single().Allocation.Quantity == 268.30336763m
                && trading.State.Live.ExternalSales.Single().Allocation.Lot.RecordedQuantity
                    - applied.AllocatedSoldQuantity
                    == .00001902m
                && trading.State.Mode == maintained.Mode
                && trading.State.Mandate == maintained.Mandate
                && trading.State.Live.CashThb == maintained.Live.CashThb
                && trading.State.Live.FundingThb == maintained.Live.FundingThb
                && trading.State.Live.Fills.Count == maintained.Live.Fills.Count
                && applied.CashSettlementPending,
            "Retirement removes phantom inventory, retains original lot and discrepancy, and never invents settlement or authority"
        );
        Check(
            TradingLedgerAccounting.Equity(trading.State.Live, [], now, 90) is null,
            "Missing settlement cannot become a known internal equity or zero profit"
        );
        var control = new TradingControl(
            trading,
            new FakeTradingExchange(),
            new FakeTradingMarketReader([]),
            new AccountContext(account),
            new TradingControlOptions("synthetic-owner"),
            new SemaphoreSlim(1, 1),
            clock
        );
        await Reject(
            () =>
                control.Activate(
                    account,
                    trading.State.RulesHash,
                    now.AddHours(1),
                    "enable-auto-trading",
                    CancellationToken.None
                ),
            "Missing settlement remains an explicit activation gate"
        );

        foreach (var afterCommit in new[] { false, true })
        {
            var failureTrading = new FakeTradingStore(state);
            var failureStore = new FakeManualSaleAllocationStore(account, failureTrading)
            {
                FailBeforeCommit = !afterCommit,
                FailAfterCommit = afterCommit,
            };
            var failure = new ManualSaleReviewService(
                new AccountContext(account),
                failureTrading,
                failureStore,
                broker,
                new ManualSaleReconciliation(
                    account,
                    failureTrading,
                    new PortfolioTestRepository(imported),
                    failureStore,
                    clock
                ),
                proofs,
                clock,
                gate
            );
            var failurePreview = await failure.Preview("WLD", execution.ExecutionId, CancellationToken.None);
            try
            {
                _ = await failure.Apply(failurePreview.Proof, failurePreview.Acknowledgement, CancellationToken.None);
                throw new InvalidOperationException("Expected synthetic atomic-save failure.");
            }
            catch (IOException) { }

            Check(
                failureTrading.State.Live.ExternalSales.Count == (afterCommit ? 1 : 0)
                    && failureStore.Journal.Receipts.Count == (afterCommit ? 1 : 0),
                "Inventory and attribution commit together or neither changes"
            );
            failureStore.FailBeforeCommit = false;
            failureStore.FailAfterCommit = false;
            _ = await failure.Apply(failurePreview.Proof, failurePreview.Acknowledgement, CancellationToken.None);
            Check(
                failureTrading.State.Live.ExternalSales.Count == 1 && failureStore.Journal.Receipts.Count == 1,
                "Explicit retry reads durable outcome and cannot retire or credit twice"
            );
        }

        var verifiedSettlement = new ManualSaleSettlement(
            account,
            "WLD",
            execution.ExecutionId,
            5012.34m,
            "synthetic-explicit-cash-credit"
        );
        var settledTrading = new FakeTradingStore(state);
        var settledStore = new FakeManualSaleAllocationStore(account, settledTrading);
        var settledBroker = new FakeManualSaleEvidenceReader(evidence with { Settlement = verifiedSettlement });
        var settled = new ManualSaleReviewService(
            new AccountContext(account),
            settledTrading,
            settledStore,
            settledBroker,
            new ManualSaleReconciliation(
                account,
                settledTrading,
                new PortfolioTestRepository(imported),
                settledStore,
                clock
            ),
            proofs,
            clock,
            gate
        );
        var settledPreview = await settled.Preview("WLD", execution.ExecutionId, CancellationToken.None);
        _ = await settled.Apply(settledPreview.Proof, settledPreview.Acknowledgement, CancellationToken.None);
        _ = await settled.Apply(settledPreview.Proof, settledPreview.Acknowledgement, CancellationToken.None);
        Check(
            settledTrading.State.Live.CashThb == state.Live.CashThb + verifiedSettlement.NetThb
                && settledTrading.State.Live.FundingThb == state.Live.FundingThb
                && settledStore.AppendCalls == 1,
            "Only evidenced cash credits once, never as funding"
        );
        var nextBuy = new TradingFill(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "new-engine-buy",
            "WLD",
            TradeSide.Buy,
            1m,
            10m,
            10m,
            0m,
            now.AddDays(1)
        );
        var afterBuy = TradingLedgerAccounting.Apply(settledTrading.State.Live, nextBuy);
        Check(
            afterBuy.Positions.Single().Quantity == 1m
                && afterBuy.Positions.Single().CostThb == 10m
                && afterBuy.ExternalSales.Single().OriginalPosition.CostThb == 4950.18m,
            "A later buy never inherits the retired lot cost"
        );
        var saleDay = Quantara.Portfolio.Application.Trading.TradingDailyReportBuilder.Build(
            settledTrading.State.Live,
            TradingMode.AutoLive,
            "2026-10-20",
            [],
            null,
            now
        );
        Check(saleDay.RealizedNetThb is null, "Unresolved external sale does not fabricate same-day profit");
        await CashBaselineChecks.Run(trading.State, account, now);
        await CashBaselineActivationJourneyChecks.Run(trading.State, account, now);
        await CashBaselineEvidenceChecks.Run(trading.State, account, now);
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

        throw new InvalidOperationException("Expected rejection: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
