using System.Globalization;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class TradingCancellationRecoveryChecks
{
    public static async Task Run()
    {
        foreach (var cancellation in new[] { "unknown", "claimed" })
        {
            foreach (var status in new[] { "cancelled", "filled" })
            {
                await TerminalRecovery(status, cancellation, false);
                await TerminalRecovery(status, cancellation, true);
            }

            await UnavailableRecovery(cancellation, false);
            await UnavailableRecovery(cancellation, true);
            await ManualSaleGuards(cancellation);
        }

        Console.WriteLine(
            "Cancellation recovery: retained terminal claims, direct/prepared inspection, unavailable outcomes and manual-sale guards passed with fake brokers only."
        );
    }

    private static async Task TerminalRecovery(string status, string cancellation, bool prepared)
    {
        using var fixture = Retained(status, cancellation);
        var before = fixture.Store.State;
        var original = before.Live.Orders.Single();
        var inspections = 0;
        fixture.Exchange.Observation = order =>
        {
            inspections++;
            return new(status, order.BrokerOrderId, order.Remaining, []);
        };
        if (prepared)
        {
            await fixture.Engine.PrepareReconciliation(CancellationToken.None);
            Check(inspections == 1, "Prepared recovery reads the retained terminal claim once.");
            Check(
                fixture.Store.State.Live.Orders.Single() == original,
                "Preparation alone does not record an unconsumed broker observation."
            );
            await fixture.Engine.Process(
                new(TradingEventKind.Reconcile, "", 0, fixture.Time.Now),
                CancellationToken.None
            );
        }
        else
        {
            await fixture.Engine.Tick(CancellationToken.None);
        }

        Check(
            inspections == 1
                && fixture.Store.State.Live.Orders.Single()
                    == original with
                    {
                        CancellationState = "confirmed",
                        LastInspectedAt = fixture.Time.Now,
                        LastInspectionError = null,
                    },
            "An exact terminal broker result confirms the cancellation while preserving the retained intent."
        );
        Preserved(fixture, before);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(inspections == 1, "A confirmed terminal order is not inspected or sent again.");
        Preserved(fixture, before);
    }

    private static async Task UnavailableRecovery(string cancellation, bool unavailable)
    {
        using var fixture = Retained("cancelled", cancellation);
        var before = fixture.Store.State;
        var terminalAvailable = false;
        var inspections = 0;
        fixture.Exchange.Observation = order =>
        {
            inspections++;
            if (terminalAvailable)
            {
                return new("cancelled", order.BrokerOrderId, order.Remaining, []);
            }

            if (unavailable)
            {
                throw new HttpRequestException("Synthetic unavailable inspection.");
            }

            return new("unknown", order.BrokerOrderId, null, [], "Synthetic exact broker match remains unavailable.");
        };
        await fixture.Engine.Tick(CancellationToken.None);
        var uncertain = fixture.Store.State.Live.Orders.Single();
        Check(
            inspections == 1
                && uncertain.Status == "unknown"
                && uncertain.CancellationState == cancellation
                && uncertain.LastInspectedAt == fixture.Time.Now
                && uncertain.LastInspectionError is not null,
            "Unavailable or unknown inspection retains cancellation uncertainty and its actual check time."
        );
        Preserved(fixture, before);
        await fixture.Engine.Tick(CancellationToken.None);
        Check(inspections == 1, "An uncertain result respects the inspection cooldown.");
        fixture.Time.Now = fixture.Time.Now.AddSeconds(31);
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with { ObservedAt = fixture.Time.Now };
        fixture.Markets.Books = [fixture.Book("BTC", false), fixture.Book("ETH", false)];
        terminalAvailable = true;
        await fixture.Engine.Tick(CancellationToken.None);
        Check(
            inspections == 2
                && fixture.Store.State.Live.Orders.Single()
                    == uncertain with
                    {
                        Status = "cancelled",
                        CancellationState = "confirmed",
                        LastInspectedAt = fixture.Time.Now,
                        LastInspectionError = null,
                    },
            "A fresh terminal result after cooldown resolves the retained uncertainty without replay."
        );
        Preserved(fixture, before);
    }

    private static async Task ManualSaleGuards(string cancellation)
    {
        using var fixture = Retained("cancelled", cancellation);
        var now = fixture.Time.Now;
        var holding = new TradingHolding("WLD", 2m, 20m, now.AddDays(-1), 10m);
        fixture.Store.State = fixture.Store.State with
        {
            Live = fixture.Store.State.Live with { Positions = [holding] },
        };
        var state = fixture.Store.State;
        var execution = new ManualSaleExecution(
            fixture.Account.AccountKey,
            "synthetic-manual-sale",
            "synthetic-manual-order",
            "WLD",
            TradeSide.Sell,
            2m,
            now.AddHours(-1),
            now,
            "synthetic-manual-evidence"
        );
        var balance = new Balance("WLD", 0m, 0m);
        var lot = new ManualSaleLot(
            fixture.Account.AccountKey,
            "WLD:" + holding.OpenedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            "WLD",
            holding.OpenedAt,
            holding.Quantity,
            holding.CostThb
        );
        var sale = new TradingExternalSale(
            holding,
            new ManualSaleAllocation(lot, execution, execution.Quantity),
            balance,
            now,
            now,
            new string('c', 64),
            null
        );
        var allocations = new FakeManualSaleAllocationStore(fixture.Account.AccountKey, fixture.Store);
        var broker = new FakeManualSaleEvidenceReader(new(execution, balance, now));
        var reconciliation = new ManualSaleReconciliation(
            fixture.Account.AccountKey,
            fixture.Store,
            new PortfolioTestRepository(new PortfolioLedger()),
            allocations,
            fixture.Time
        );
        var workflow = new ManualSaleReviewService(
            fixture.Account,
            fixture.Store,
            allocations,
            broker,
            reconciliation,
            new ManualSaleReviewProof(new TradingControlOptions("synthetic-cancellation-owner")),
            fixture.Time,
            fixture.Gate
        );
        var before = JsonSerializer.Serialize(state);
        var previewRejected = false;
        try
        {
            _ = await workflow.Preview("WLD", execution.ExecutionId, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            previewRejected = true;
        }

        var retirementRejected = false;
        try
        {
            _ = TradingManualSaleInventory.Retire(state, sale, fixture.Account.AccountKey, now);
        }
        catch (InvalidOperationException)
        {
            retirementRejected = true;
        }

        Check(
            previewRejected
                && retirementRejected
                && broker.Reads == 0
                && allocations.AppendCalls == 0
                && fixture.Store.History.Count == 1
                && JsonSerializer.Serialize(fixture.Store.State) == before,
            "An unrelated terminal cancellation claim blocks sale preview and retirement without reading broker evidence or writing state."
        );
        fixture.Store.State = state with
        {
            Live = state.Live with { Orders = [state.Live.Orders.Single() with { CancellationState = "confirmed" }] },
        };
        var preview = await workflow.Preview("WLD", execution.ExecutionId, CancellationToken.None);
        var retired = TradingManualSaleInventory.Retire(fixture.Store.State, sale, fixture.Account.AccountKey, now);
        Check(
            !preview.Applied
                && broker.Reads == 1
                && retired.Live.Positions.Count == 0
                && retired.Live.ExternalSales.Count == 1
                && retired.Live.CashThb == state.Live.CashThb
                && retired.Live.FundingThb == state.Live.FundingThb
                && retired.Mode == state.Mode
                && retired.Mandate == state.Mandate
                && allocations.AppendCalls == 0
                && fixture.Store.History.Count == 1,
            "The same complete manual-sale evidence becomes eligible when the unrelated cancellation is confirmed."
        );
        Preserved(fixture, state);
    }

    private static AutonomousFixture Retained(string status, string cancellation)
    {
        var fixture = new AutonomousFixture();
        var now = fixture.Time.Now;
        var state = fixture.Store.State;
        var order = new TradingIntent(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100.25m,
            100m,
            status == "filled" ? 0m : 100.25m,
            now.AddMinutes(-10),
            0,
            state.RulesHash,
            "synthetic-retained-terminal-order",
            status,
            "synthetic-broker-order",
            CancellationState: cancellation,
            LastInspectedAt: now.AddSeconds(-31)
        )
        {
            LastInspectionError = "Retained cancellation outcome requires inspection.",
        };
        TradingFill[] fills =
            status == "filled"
                ?
                [
                    new TradingFill(
                        Guid.NewGuid(),
                        order.Id,
                        "synthetic-retained-fill",
                        "BTC",
                        TradeSide.Buy,
                        1m,
                        100m,
                        100m,
                        0.25m,
                        now.AddMinutes(-9)
                    ),
                ]
                : [];
        fixture.Store.State = state with
        {
            Mode = TradingMode.Stopped,
            EmergencyStopped = true,
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                fixture.Account.AccountKey,
                state.RulesHash,
                now.AddHours(-2),
                now.AddHours(1),
                now.AddSeconds(-10)
            ),
            Live = state.Live with
            {
                OpenedAt = now.AddDays(-1),
                CashThb = 2000.25m,
                FundingThb = status == "filled" ? 2100.50m : 2000.25m,
                Orders = [order],
                Fills = fills,
                Positions =
                    status == "filled" ? [new TradingHolding("BTC", 1m, 100.25m, now.AddMinutes(-9), 100m)] : [],
            },
        };
        fixture.Evidence.Evidence = fixture.Evidence.Evidence with
        {
            Balances =
            [
                new Balance("THB", 5000m, 5000m),
                new Balance("BTC", status == "filled" ? 1m : 0m, status == "filled" ? 1m : 0m),
            ],
        };
        fixture.Markets.Books = [fixture.Book("BTC", false), fixture.Book("ETH", false)];
        return fixture;
    }

    private static void Preserved(AutonomousFixture fixture, TradingState before)
    {
        Check(
            fixture.Store.State.Live.CashThb == before.Live.CashThb
                && fixture.Store.State.Live.FundingThb == before.Live.FundingThb
                && fixture.Store.State.Live.Fills.SequenceEqual(before.Live.Fills)
                && fixture.Store.State.Mode == before.Mode
                && fixture.Store.State.Mandate == before.Mandate
                && fixture.Store.State.EmergencyStopped == before.EmergencyStopped
                && fixture.Exchange.Submissions == 0
                && fixture.Exchange.Cancellations == 0,
            "Read-only recovery preserves cash, funding, retained fills and disarmed authority without broker sends."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
