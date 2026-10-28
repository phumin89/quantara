using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingNotificationReceiptSqlChecks
{
    public static async Task Run(bool legacy = false)
    {
        var connectionString = Environment.GetEnvironmentVariable("QUANTARA_FEATURE_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Trading notification receipt SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        if (legacy)
        {
            connectionString = await CryptoSqlTestDatabase.Connection("QUANTARA_FEATURE_SQL_CONNECTION");
        }

        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!connection.InitialCatalog.StartsWith("QuantaraFeatureChecks", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Receipt SQL checks require an isolated QuantaraFeatureChecks database."
            );
        }

        await using var database = new SqlConnection(connectionString);
        await database.OpenAsync();
        if (legacy)
        {
            await using var schema = new SqlCommand(
                "IF OBJECT_ID(N'dbo.TradingNotificationReceipts',N'U') IS NOT NULL THROW 51000,'Legacy receipt fixture requires an absent table; existing data was not changed.',1; CREATE TABLE dbo.TradingNotificationReceipts(AccountKey char(64) COLLATE Latin1_General_100_CI_AS NOT NULL,DestinationKey nvarchar(200) COLLATE Latin1_General_100_CI_AS NOT NULL,AlertKey nvarchar(200) COLLATE Latin1_General_100_CI_AS NOT NULL,ClaimedAt datetimeoffset(7) NOT NULL,MessageId bigint NOT NULL CHECK(MessageId>0),SentAt datetimeoffset(7) NOT NULL,CHECK(SentAt>=ClaimedAt),PRIMARY KEY(AccountKey,DestinationKey,AlertKey,ClaimedAt));",
                database
            );
            await schema.ExecuteNonQueryAsync();
        }

        var account = "a" + Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant()[1..];
        var destination = "receipt-" + Guid.NewGuid().ToString("N");
        var store = new SqlTradingNotificationReceiptStore(connectionString, account, destination);
        await store.Initialize(CancellationToken.None);
        await using var metadata = new SqlCommand(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.TradingNotificationReceipts') AND name IN(N'AccountKey',N'DestinationKey',N'AlertKey') AND collation_name=@collation;",
            database
        );
        metadata.Parameters.Add("@collation", SqlDbType.NVarChar, 128).Value = legacy
            ? "Latin1_General_100_CI_AS"
            : "Latin1_General_100_BIN2";
        Check(
            (int)(await metadata.ExecuteScalarAsync() ?? 0) == 3,
            "Receipt fixture must prove its starting identity collation."
        );
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
        var offsetClaim = now.ToOffset(TimeSpan.FromHours(7));
        await store.Save(new(receipt.Key, offsetClaim, receipt.MessageId, receipt.SentAt), CancellationToken.None);
        var offsetRead =
            await store.Read(receipt.Key, offsetClaim, CancellationToken.None)
            ?? throw new InvalidOperationException("Same-instant receipt lookup lost its acknowledgement.");
        Check(
            offsetRead.ClaimedAt.EqualsExact(now) && offsetRead.SentAt.EqualsExact(receipt.SentAt),
            "Same-instant claim offsets must resolve without fabricating a requested offset or changing retained precision."
        );
        foreach (
            var dimension in legacy
                ? new[] { "account-case", "destination-case", "alert-case", "destination-padding", "alert-padding" }
                : new[] { "destination-padding", "alert-padding" }
        )
        {
            await CheckAlias(database, connectionString, account, destination, now, dimension);
        }

        Console.WriteLine(
            $"Trading notification receipt SQL checks passed: {(legacy ? "legacy CI" : "BIN2")} identity, retained offsets, alias rejection and no-resend recovery."
        );
    }

    private static async Task CheckAlias(
        SqlConnection database,
        string connectionString,
        string account,
        string destination,
        DateTimeOffset now,
        string dimension
    )
    {
        var key = "Alias-" + Guid.NewGuid().ToString("N");
        var storedAccount = dimension == "account-case" ? account.ToUpperInvariant() : account;
        var storedDestination =
            dimension == "destination-case" ? destination.ToUpperInvariant()
            : dimension == "destination-padding" ? destination + " "
            : destination;
        var storedKey =
            dimension == "alert-case" ? key.ToLowerInvariant()
            : dimension == "alert-padding" ? key + " "
            : key;
        var receipt = new TradingNotificationReceipt(key, now, 42, now.AddSeconds(1));
        await using var seed = new SqlCommand(
            "INSERT INTO dbo.TradingNotificationReceipts(AccountKey,DestinationKey,AlertKey,ClaimedAt,MessageId,SentAt) VALUES(@account,@destination,@alert,@claim,@message,@sent);",
            database
        );
        seed.Parameters.Add("@account", SqlDbType.Char, 64).Value = storedAccount;
        seed.Parameters.Add("@destination", SqlDbType.NVarChar, 200).Value = storedDestination;
        seed.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = storedKey;
        seed.Parameters.Add("@claim", SqlDbType.DateTimeOffset).Value = receipt.ClaimedAt;
        seed.Parameters.Add("@message", SqlDbType.BigInt).Value = receipt.MessageId;
        seed.Parameters.Add("@sent", SqlDbType.DateTimeOffset).Value = receipt.SentAt;
        Check(await seed.ExecuteNonQueryAsync() == 1, "Seed exactly one isolated retained alias.");
        var before = await ReadStored(database, storedAccount, storedDestination, storedKey, now);
        var store = new SqlTradingNotificationReceiptStore(connectionString, account, destination);
        Check(
            await store.Read(key, now, CancellationToken.None) is null,
            "A native SQL alias is not an exact acknowledgement."
        );
        await ExpectConflict(() => store.Save(receipt, CancellationToken.None));
        var runtime = new FakeTradingDeliveryStore(
            TradingStateFactory.Create(now) with
            {
                Alerts = [new TradingAlert(key, "fill", "isolated receipt alias", now, "sending") { ClaimedAt = now }],
            }
        );
        var notifier = new FakeTradingNotifier();
        var delivery = new TradingReportDelivery(
            runtime,
            notifier,
            new FixedTimeProvider(now.AddMinutes(3)),
            receipts: store
        );
        await delivery.DeliverAlert(key, CancellationToken.None);
        await delivery.DeliverAlert(key, CancellationToken.None);
        Check(
            notifier.Sends == 0
                && notifier.Edits == 0
                && runtime.State.Alerts.Single() is { Status: "unknown", MessageId: null, SentAt: null } retained
                && retained.ClaimedAt == now,
            "Alias proof must leave the exact send uncertain, without a resend or false sent outcome."
        );
        Check(
            await ReadStored(database, storedAccount, storedDestination, storedKey, now) == before,
            "A rejected alias must not alter or replace any retained acknowledgement."
        );
    }

    private static async Task<string> ReadStored(
        SqlConnection database,
        string account,
        string destination,
        string key,
        DateTimeOffset claim
    )
    {
        await using var command = new SqlCommand(
            "SELECT AccountKey,DestinationKey,AlertKey,ClaimedAt,MessageId,SentAt FROM dbo.TradingNotificationReceipts WHERE AccountKey=@account AND DestinationKey=@destination AND AlertKey=@alert AND ClaimedAt=@claim;",
            database
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@destination", SqlDbType.NVarChar, 200).Value = destination;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = key;
        command.Parameters.Add("@claim", SqlDbType.DateTimeOffset).Value = claim;
        await using var reader = await command.ExecuteReaderAsync();
        Check(await reader.ReadAsync(), "Retained alias fixture must remain present.");
        var snapshot = JsonSerializer.Serialize(
            new
            {
                Account = reader.GetString(0),
                Destination = reader.GetString(1),
                Key = reader.GetString(2),
                Claim = reader.GetDateTimeOffset(3),
                Message = reader.GetInt64(4),
                Sent = reader.GetDateTimeOffset(5),
            }
        );
        Check(!await reader.ReadAsync(), "Retained fixture identity must still have exactly one row.");
        return snapshot;
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
