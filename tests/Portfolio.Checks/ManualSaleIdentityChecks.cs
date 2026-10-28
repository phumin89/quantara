using System.Text.Json;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public static class ManualSaleIdentityChecks
{
    public static void Run()
    {
        var account = new string('a', 64);
        var now = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var bitcoinLot = new ManualSaleLot(account, "BTC:first-lot", "BTC", now.AddHours(-3), 2m, 200m, 2m);
        var etherLot = new ManualSaleLot(account, "ETH:first-lot", "ETH", now.AddHours(-3), 3m, 300m, 3m);
        var bitcoin = new ManualSaleExecution(
            account,
            "shared-raw-id",
            "bitcoin-order",
            "BTC",
            TradeSide.Sell,
            1.5m,
            now.AddHours(-1),
            now.AddMinutes(-1),
            "bitcoin-execution-evidence"
        );
        var ether = bitcoin with
        {
            OrderId = "ether-order",
            Symbol = "ETH",
            Quantity = 2m,
            OccurredAt = now.AddMinutes(-59),
            EvidenceReference = "ether-execution-evidence",
        };
        foreach (var reverse in new[] { false, true })
        {
            var firstLot = reverse ? etherLot : bitcoinLot;
            var firstExecution = reverse ? ether : bitcoin;
            var secondLot = reverse ? bitcoinLot : etherLot;
            var secondExecution = reverse ? bitcoin : ether;
            var first = ManualSaleAllocationPlanner.Allocate(firstLot, firstExecution, firstExecution.Quantity, []);
            var combined = ManualSaleAllocationPlanner.Allocate(
                secondLot,
                secondExecution,
                secondExecution.Quantity,
                first.Journal
            );
            var repeated = ManualSaleAllocationPlanner.Allocate(
                firstLot,
                firstExecution,
                firstExecution.Quantity,
                combined.Journal
            );
            if (
                combined.Journal.Count != 2
                || !combined
                    .Journal.Select(allocation => allocation.Execution.Symbol)
                    .ToHashSet()
                    .SetEquals(["BTC", "ETH"])
                || combined.Journal.Any(allocation => allocation.Execution.ExecutionId != "shared-raw-id")
                || !repeated.Journal.SequenceEqual(combined.Journal)
            )
            {
                throw new InvalidOperationException(
                    "Symbol-qualified execution identities coexist and repeat without replacing or duplicating entries in either order."
                );
            }

            var journal = new ManualSaleAllocationJournal(
                account,
                combined.Journal.Count,
                combined
                    .Journal.Select(allocation => new ManualSaleAllocationReceipt(
                        allocation,
                        "owner-review-" + allocation.Execution.Symbol,
                        now,
                        now.AddMinutes(5)
                    ))
                    .ToArray()
            );
            var payload = JsonSerializer.SerializeToUtf8Bytes(journal);
            journal.Validate(account);
            var restored =
                JsonSerializer.Deserialize<ManualSaleAllocationJournal>(payload)
                ?? throw new InvalidOperationException("Symbol-qualified journal did not deserialize.");
            restored.Validate(account);
            if (!payload.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(restored)))
            {
                throw new InvalidOperationException("Journal roundtrip must preserve exact identities and bytes.");
            }
        }

        var firstAllocation = new ManualSaleAllocation(bitcoinLot, bitcoin, 0.5m);
        var secondBitcoinLot = bitcoinLot with { LotKey = "BTC:second-lot", OpenedAt = now.AddHours(-2) };
        var split = ManualSaleAllocationPlanner.Allocate(secondBitcoinLot, bitcoin, 1m, [firstAllocation]);
        if (split.Journal.Count != 2 || split.Journal.Sum(allocation => allocation.Quantity) != bitcoin.Quantity)
        {
            throw new InvalidOperationException(
                "One execution may be split across lots only up to its exact quantity."
            );
        }

        new ManualSaleAllocationJournal(
            account,
            split.Journal.Count,
            split
                .Journal.Select(allocation => new ManualSaleAllocationReceipt(allocation, "split-review", now))
                .ToArray()
        ).Validate(account);
        (string Name, ManualSaleExecution Execution)[] conflicts =
        [
            ("order", bitcoin with { OrderId = "conflicting-order" }),
            ("evidence", bitcoin with { EvidenceReference = "conflicting-evidence" }),
            ("quantity", bitcoin with { Quantity = 1.75m }),
            ("execution time", bitcoin with { OccurredAt = bitcoin.OccurredAt.AddMinutes(1) }),
            ("observation time", bitcoin with { ObservedAt = bitcoin.ObservedAt.AddSeconds(1) }),
        ];
        var rejections = new List<(string Name, Action Validate)>();
        foreach (var conflict in conflicts)
        {
            rejections.Add(
                (
                    "same-symbol conflicting " + conflict.Name,
                    () =>
                        ManualSaleAllocationPlanner.Allocate(
                            secondBitcoinLot,
                            conflict.Execution,
                            0.5m,
                            [firstAllocation]
                        )
                )
            );
            rejections.Add(
                (
                    "journal with same-symbol conflicting " + conflict.Name,
                    () =>
                        new ManualSaleAllocationJournal(
                            account,
                            2,
                            [
                                new ManualSaleAllocationReceipt(firstAllocation, "original-review", now),
                                new ManualSaleAllocationReceipt(
                                    new ManualSaleAllocation(secondBitcoinLot, conflict.Execution, 0.5m),
                                    "conflicting-review",
                                    now
                                ),
                            ]
                        ).Validate(account)
                )
            );
        }

        var foreignAccount = new string('b', 64);
        var foreignLot = etherLot with { AccountKey = foreignAccount };
        var foreignExecution = ether with { AccountKey = foreignAccount };
        rejections.Add(
            (
                "execution quantity exceeded across lots",
                () => ManualSaleAllocationPlanner.Allocate(secondBitcoinLot, bitcoin, 1.01m, [firstAllocation])
            )
        );
        rejections.Add(
            (
                "cross-account allocation journal",
                () => ManualSaleAllocationPlanner.Allocate(foreignLot, foreignExecution, 1m, [firstAllocation])
            )
        );
        rejections.Add(
            (
                "cross-account retained journal",
                () =>
                    new ManualSaleAllocationJournal(
                        account,
                        2,
                        [
                            new ManualSaleAllocationReceipt(firstAllocation, "owner-review", now),
                            new ManualSaleAllocationReceipt(
                                new ManualSaleAllocation(foreignLot, foreignExecution, 1m),
                                "foreign-review",
                                now
                            ),
                        ]
                    ).Validate(account)
            )
        );
        foreach (var rejection in rejections)
        {
            var rejected = false;
            try
            {
                rejection.Validate();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new InvalidOperationException("Expected manual-sale rejection: " + rejection.Name + ".");
            }
        }

        var uniqueJournal = new ManualSaleAllocationJournal(
            account,
            2,
            [
                new ManualSaleAllocationReceipt(firstAllocation, "retained-bitcoin-review", now),
                new ManualSaleAllocationReceipt(
                    new ManualSaleAllocation(etherLot, ether with { ExecutionId = "unique-ether-id" }, 1m),
                    "retained-ether-review",
                    now
                ),
            ]
        );
        var retained = JsonSerializer.SerializeToUtf8Bytes(uniqueJournal);
        uniqueJournal.Validate(account);
        if (!retained.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(uniqueJournal)))
        {
            throw new InvalidOperationException("Valid unique-ID journal validation cannot rewrite retained bytes.");
        }

        Console.WriteLine(
            "Manual-sale identity: symbol-qualified attribution, exact evidence, shared quantity caps and retained journal bytes passed."
        );
    }
}
