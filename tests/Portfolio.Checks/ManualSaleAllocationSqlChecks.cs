using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class ManualSaleAllocationSqlChecks
{
    public static async Task Run()
    {
        var connectionString = await CryptoSqlTestDatabase.Connection("QUANTARA_MANUAL_SALE_TEST_SQL");

        var account = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("manual-sale-fixture:" + Guid.NewGuid())))
            .ToLowerInvariant();
        var now = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        var trading = new SqlTradingStore(connectionString, account, new FixedTimeProvider(now));
        await trading.Initialize(CancellationToken.None);
        var state = await trading.Read(CancellationToken.None);
        if (state.Revision != 0)
        {
            throw new InvalidOperationException("Expected a new synthetic account; retained records were not reused.");
        }

        var stopped = state with { Revision = 1, Mode = TradingMode.Stopped };
        Check(
            await trading.Save(stopped, state.Revision, CancellationToken.None),
            "Initialize synthetic stopped state"
        );
        var before = JsonSerializer.Serialize(await trading.Read(CancellationToken.None));
        var store = new SqlManualSaleAllocationStore(connectionString, account, new FixedTimeProvider(now));
        await store.Initialize(CancellationToken.None);
        var lot = new ManualSaleLot(account, "fixture-lot", "WLD", now.AddHours(-2), 268.30338665m, 4950.18m);
        var execution = new ManualSaleExecution(
            account,
            "fixture-sale",
            "fixture-order",
            "WLD",
            TradeSide.Sell,
            268.30336763m,
            now.AddHours(-1),
            now.AddMinutes(-1),
            "synthetic-broker-receipt"
        );
        var receipt = new ManualSaleAllocationReceipt(
            new ManualSaleAllocation(lot, execution, execution.Quantity),
            "fixture-owner-review",
            now
        );
        var results = await Task.WhenAll(
            store.Append(receipt, 0, 1, CancellationToken.None),
            store.Append(receipt, 0, 1, CancellationToken.None)
        );
        Check(results.Count(result => result) == 1, "Exactly one concurrent journal CAS wins");
        var restarted = new SqlManualSaleAllocationStore(connectionString, account, new FixedTimeProvider(now));
        var retained = await restarted.Read(CancellationToken.None);
        Check(
            retained.Revision == 1 && retained.Receipts.Single() == receipt,
            "Exact decimals and audit identities survive restart"
        );
        Check(
            !await restarted.Append(receipt, 0, 1, CancellationToken.None),
            "Stale journal revision never duplicates attribution"
        );
        var nextExecution = execution with { ExecutionId = "next-fixture-sale", Quantity = .00001902m };
        var nextReceipt = receipt with
        {
            Allocation = new ManualSaleAllocation(lot, nextExecution, nextExecution.Quantity),
        };
        Check(
            !await restarted.Append(nextReceipt, 1, 0, CancellationToken.None),
            "Stale trading revision cannot append"
        );
        Check(
            (await restarted.Read(CancellationToken.None)).Revision == 1,
            "Rejected claims preserve journal revision"
        );
        var expired = nextReceipt with { RecordedAt = now.AddSeconds(-2), ReviewExpiresAt = now.AddSeconds(-1) };
        try
        {
            _ = await restarted.Append(expired, 1, 1, CancellationToken.None);
            throw new InvalidDataException("Expired review was unexpectedly committed.");
        }
        catch (InvalidOperationException)
        {
            Check(
                (await restarted.Read(CancellationToken.None)).Revision == 1,
                "Review expiry is checked after SQL lock acquisition"
            );
        }

        Check(
            JsonSerializer.Serialize(await trading.Read(CancellationToken.None)) == before,
            "Audit storage never changes trading runtime, holdings or authority"
        );
        await WriteBound(connectionString, account, receipt, restarted);
        await InventoryTransition(connectionString, now);
        Console.WriteLine(
            "Manual-sale isolated SQL checks passed: concurrent CAS, account journal restart, exact evidence and unchanged runtime. Synthetic rows retained."
        );
    }

    private static async Task InventoryTransition(string connectionString, DateTimeOffset now)
    {
        foreach (var forceMirrorFailure in new[] { true, false })
        {
            var account = Convert
                .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("inventory-fixture:" + Guid.NewGuid())))
                .ToLowerInvariant();
            var clock = new FixedTimeProvider(now);
            var trading = new SqlTradingStore(connectionString, account, clock);
            await trading.Initialize(CancellationToken.None);
            var initial = await trading.Read(CancellationToken.None);
            var holding = new TradingHolding("WLD", 268.30338665m, 4950.18m, now.AddHours(-2), 18.45m);
            var stopped = initial with
            {
                Revision = initial.Revision + 1,
                Mode = TradingMode.Stopped,
                Live = initial.Live with { FundingThb = 5000m, CashThb = 49.82m, Positions = [holding] },
            };
            Check(
                await trading.Save(stopped, initial.Revision, CancellationToken.None),
                "Prepare isolated owned inventory"
            );
            var before = JsonSerializer.Serialize(await trading.Read(CancellationToken.None));
            var execution = new ManualSaleExecution(
                account,
                "isolated-sale",
                "isolated-order",
                "WLD",
                TradeSide.Sell,
                268.30336763m,
                now.AddHours(-1),
                now,
                "synthetic-confirmed-order"
            );
            var lot = new ManualSaleLot(
                account,
                "WLD:" + holding.OpenedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "WLD",
                holding.OpenedAt,
                holding.Quantity,
                holding.CostThb
            );
            var review = new string('a', 64);
            var receipt = new ManualSaleAllocationReceipt(
                new ManualSaleAllocation(lot, execution, execution.Quantity),
                review,
                now,
                now.AddMinutes(5)
            );
            var retirement = new TradingExternalSale(
                holding,
                receipt.Allocation,
                new Balance("WLD", 0m, 0m),
                now,
                now,
                review,
                null
            );
            var store = new SqlManualSaleAllocationStore(connectionString, account, clock);
            await store.Initialize(CancellationToken.None);
            if (forceMirrorFailure)
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(CancellationToken.None);
                await using var conflict = new SqlCommand(
                    "INSERT INTO dbo.TradingPositions(AccountKey,Id,Payload) VALUES(@account,@id,@payload);",
                    connection
                );
                conflict.Parameters.Add("@account", System.Data.SqlDbType.Char, 64).Value = account;
                conflict.Parameters.Add("@id", System.Data.SqlDbType.NVarChar, 200).Value =
                    "AutoLive:external:" + review;
                conflict.Parameters.Add("@payload", System.Data.SqlDbType.NVarChar, -1).Value =
                    "{\"syntheticConflict\":true}";
                await conflict.ExecuteNonQueryAsync(CancellationToken.None);
                try
                {
                    _ = await store.Append(receipt, 0, stopped.Revision, CancellationToken.None, retirement);
                    throw new InvalidOperationException("The synthetic immutable mirror conflict was accepted.");
                }
                catch (InvalidDataException)
                {
                    Check(
                        (await store.Read(CancellationToken.None)).Revision == 0
                            && JsonSerializer.Serialize(await trading.Read(CancellationToken.None)) == before,
                        "Mirror failure rolls back journal, runtime and inventory together"
                    );
                }
            }
            else
            {
                var attempts = await Task.WhenAll(
                    store.Append(receipt, 0, stopped.Revision, CancellationToken.None, retirement),
                    store.Append(receipt, 0, stopped.Revision, CancellationToken.None, retirement)
                );
                Check(
                    attempts.Count(result => result) == 1,
                    "One concurrent inventory retirement wins both revision claims"
                );
                var restarted = new SqlTradingStore(connectionString, account, clock);
                var retained = await restarted.Read(CancellationToken.None);
                Check(
                    retained.Revision == stopped.Revision + 1
                        && retained.Live.Positions.Count == 0
                        && retained.Live.ExternalSales.Single().OriginalPosition == holding
                        && retained.Live.CashThb == stopped.Live.CashThb
                        && retained.Live.FundingThb == stopped.Live.FundingThb,
                    "Restart retains exact original lot and empty inventory without inventing cash or deposits"
                );
                Check(
                    (await store.Read(CancellationToken.None)).Receipts.Single() == receipt
                        && !await store.Append(receipt, 0, stopped.Revision, CancellationToken.None, retirement),
                    "A stale retry cannot duplicate allocation or retirement"
                );
                await CashBaselineTransition(connectionString, account, retained, now);
            }

            await using var verifyConnection = new SqlConnection(connectionString);
            await verifyConnection.OpenAsync(CancellationToken.None);
            await using var mirror = new SqlCommand(
                "SELECT JSON_VALUE(Payload,'$.position.quantity') FROM dbo.TradingPositions WHERE AccountKey=@account AND Id=N'AutoLive:WLD';",
                verifyConnection
            );
            mirror.Parameters.Add("@account", System.Data.SqlDbType.Char, 64).Value = account;
            var quantity = Convert.ToDecimal(
                await mirror.ExecuteScalarAsync(CancellationToken.None),
                System.Globalization.CultureInfo.InvariantCulture
            );
            Check(
                quantity == (forceMirrorFailure ? holding.Quantity : 0m),
                "Position mirror changes only with the complete atomic retirement"
            );
        }
    }

    private static async Task CashBaselineTransition(
        string connectionString,
        string account,
        TradingState retired,
        DateTimeOffset now
    )
    {
        var trading = new SqlTradingStore(connectionString, account, new FixedTimeProvider(now));
        var orders = new SqlOrderStore(connectionString, account);
        await orders.Initialize(CancellationToken.None);
        var stopped = retired with
        {
            Revision = retired.Revision + 1,
            Mandate = new TradingMandate(
                Guid.NewGuid(),
                account,
                retired.RulesHash,
                now.AddHours(-2),
                now.AddHours(1),
                now.AddHours(-1)
            ),
        };
        Check(
            await trading.Save(stopped, retired.Revision, CancellationToken.None),
            "Prepare synthetic revoked cash review"
        );
        var baseline = new TradingCashBaseline(
            account,
            stopped.Live.CashThb,
            5096.69m,
            5096.69m,
            stopped.Live.FundingThb,
            now,
            now,
            now,
            now.AddMinutes(5),
            stopped.RulesHash,
            new string('a', 64),
            TradingCashBaselineAccounting.Binding(stopped),
            new string('b', 64),
            stopped.Live.ExternalSales.Select(sale => sale.ReviewReference).ToArray()
        );
        var candidate = TradingCashBaselineAccounting.Adopt(stopped, baseline, account, now);
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var conflict = new SqlCommand(
                "INSERT INTO dbo.TradingRiskCounters(AccountKey,Id,Payload) VALUES(@account,@id,N'{}');",
                connection
            );
            conflict.Parameters.Add("@account", System.Data.SqlDbType.Char, 64).Value = account;
            conflict.Parameters.Add("@id", System.Data.SqlDbType.NVarChar, 200).Value =
                "AutoLive:cash-baseline:" + baseline.ReviewReference;
            await conflict.ExecuteNonQueryAsync(CancellationToken.None);
        }

        try
        {
            _ = await trading.Save(candidate, stopped.Revision, CancellationToken.None);
            throw new InvalidOperationException("Expected immutable cash mirror conflict.");
        }
        catch (InvalidDataException)
        {
            Check(
                JsonSerializer.Serialize(await trading.Read(CancellationToken.None))
                    == JsonSerializer.Serialize(stopped),
                "Cash mirror failure rolls back cash, baseline and runtime together"
            );
        }

        var reviewed = baseline with { ReviewReference = new string('c', 64) };
        candidate = TradingCashBaselineAccounting.Adopt(stopped, reviewed, account, now);
        var results = await Task.WhenAll(
            trading.Save(candidate, stopped.Revision, CancellationToken.None),
            trading.Save(candidate, stopped.Revision, CancellationToken.None)
        );
        Check(results.Count(result => result) == 1, "Exactly one concurrent cash baseline CAS wins");
        var restarted = new SqlTradingStore(connectionString, account, new FixedTimeProvider(now));
        var retained = await restarted.Read(CancellationToken.None);
        Check(
            retained.Live.CashThb == 5096.69m
                && retained.Live.FundingThb == stopped.Live.FundingThb
                && retained.Live.CashBaselines.Count == 1
                && retained.Live.ExternalSales.Single().Settlement is null
                && retained.Mandate == stopped.Mandate
                && retained.Mode == TradingMode.Stopped,
            "Restart preserves cash replacement, unknown history and revoked authority without duplicate funding"
        );
    }

    private static async Task WriteBound(
        string connectionString,
        string account,
        ManualSaleAllocationReceipt first,
        SqlManualSaleAllocationStore store
    )
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var receipts = new List<ManualSaleAllocationReceipt> { first };
        ManualSaleAllocationReceipt? overflowing = null;
        for (var index = 1; index < 1024; index++)
        {
            var identity = index.ToString(System.Globalization.CultureInfo.InvariantCulture) + new string('界', 240);
            var lot = first.Allocation.Lot with { LotKey = identity };
            var execution = first.Allocation.Execution with
            {
                ExecutionId = identity,
                OrderId = identity,
                EvidenceReference = identity,
                Quantity = 1m,
            };
            var next = first with
            {
                Allocation = new ManualSaleAllocation(lot, execution, 1m),
                ReviewReference = identity,
            };
            var trial = new ManualSaleAllocationJournal(account, receipts.Count + 1, receipts.Append(next).ToArray());
            trial.Validate(account);
            if (JsonSerializer.Serialize(trial, options).Length > 2 * 1024 * 1024)
            {
                overflowing = next;
                break;
            }

            receipts.Add(next);
        }

        if (overflowing is null)
        {
            throw new InvalidOperationException("Synthetic journal did not reach the serialized write bound.");
        }

        var seed = new ManualSaleAllocationJournal(account, receipts.Count, receipts.ToArray());
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new SqlCommand(
            "UPDATE dbo.ManualSaleAllocationJournals SET Revision=@revision,Payload=@payload WHERE AccountKey=@account AND Revision=1;",
            connection
        );
        command.Parameters.Add("@account", System.Data.SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@revision", System.Data.SqlDbType.BigInt).Value = seed.Revision;
        command.Parameters.Add("@payload", System.Data.SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            seed,
            options
        );
        Check(
            await command.ExecuteNonQueryAsync(CancellationToken.None) == 1,
            "Seed only the new synthetic journal within its read bound"
        );
        try
        {
            _ = await store.Append(overflowing, seed.Revision, 1, CancellationToken.None);
            throw new InvalidOperationException("Expected over-bound journal rejection.");
        }
        catch (InvalidDataException)
        {
            Check(
                (await store.Read(CancellationToken.None)).Revision == seed.Revision,
                "Oversized append never commits an unreadable journal or changes its revision"
            );
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
