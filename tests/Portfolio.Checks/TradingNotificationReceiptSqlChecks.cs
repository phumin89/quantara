using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingNotificationReceiptSqlChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Trading notification receipt SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!connection.InitialCatalog.StartsWith("QuantaraFeatureChecks", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Receipt SQL checks require an isolated QuantaraFeatureChecks database."
            );
        }

        var account = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();
        var destination = "receipt-" + Guid.NewGuid().ToString("N");
        var store = new SqlTradingNotificationReceiptStore(connectionString, account, destination);
        await store.Initialize(CancellationToken.None);
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var receipt = new TradingNotificationReceipt(
            "Exact-" + Guid.NewGuid().ToString("N"),
            now,
            42,
            now.AddSeconds(1)
        );
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.Save(receipt, CancellationToken.None)));
        Check(
            await store.Read(receipt.Key, now, CancellationToken.None) == receipt,
            "Concurrent identical receipt writes retain one exact acknowledgement and subsecond claim."
        );
        await ExpectConflict(() => store.Save(new(receipt.Key, now, 43, receipt.SentAt), CancellationToken.None));
        await ExpectConflict(() =>
            store.Save(new(receipt.Key, now, 42, receipt.SentAt.AddSeconds(1)), CancellationToken.None)
        );
        Check(
            await store.Read(receipt.Key, now, CancellationToken.None) == receipt,
            "Conflicting message IDs or timestamps cannot replace the original acknowledgement."
        );

        var otherDestination = new SqlTradingNotificationReceiptStore(
            connectionString,
            account,
            destination + "-other"
        );
        var otherAccount = new SqlTradingNotificationReceiptStore(connectionString, new string('b', 64), destination);
        Check(
            await otherDestination.Read(receipt.Key, now, CancellationToken.None) is null
                && await otherAccount.Read(receipt.Key, now, CancellationToken.None) is null
                && await store.Read(receipt.Key.ToLowerInvariant(), now, CancellationToken.None) is null
                && await store.Read(receipt.Key, now.AddTicks(1), CancellationToken.None) is null,
            "Receipts are isolated by exact account, destination, alert key and claim timestamp."
        );
        await otherDestination.Save(new(receipt.Key, now, 43, receipt.SentAt), CancellationToken.None);
        await otherAccount.Save(new(receipt.Key, now, 44, receipt.SentAt), CancellationToken.None);
        Check(
            await store.Read(receipt.Key, now, CancellationToken.None) == receipt,
            "Separate account and destination records cannot alter the original receipt."
        );
        Console.WriteLine("Trading notification receipt SQL checks passed.");
    }

    private static async Task ExpectConflict(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (SqlException exception) when (exception.Number == 51000)
        {
            return;
        }

        throw new InvalidOperationException("A conflicting immutable receipt must be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
