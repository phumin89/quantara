using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Infrastructure.Trading.Jobs;

namespace Portfolio.Checks;

public static class TradingDispatchMigrationSqlChecks
{
    public static async Task Run()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QUANTARA_DISPATCH_MIGRATION_SQL_CONNECTION")))
        {
            Console.WriteLine("Trading dispatch migration SQL checks skipped (opt-in connection is not configured).");
            return;
        }

        var shape =
            Environment.GetEnvironmentVariable("QUANTARA_DISPATCH_MIGRATION_START_SCHEMA") ?? "existing-destination";
        if (shape is not ("existing-destination" or "missing-destination"))
        {
            throw new InvalidOperationException(
                "Select an existing-destination or missing-destination migration fixture."
            );
        }

        var existingDestination = shape == "existing-destination";
        var connectionString = await CryptoSqlTestDatabase.Connection("QUANTARA_DISPATCH_MIGRATION_SQL_CONNECTION");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        Check(
            !await Exists(connection),
            "Migration fixtures require an absent dispatch table; existing rows are never removed."
        );

        var account = AccountKey();
        var otherAccount = AccountKey();
        var now = DateTimeOffset.UtcNow;
        await SeedLegacy(connection, account, otherAccount, now, existingDestination);
        var beforeRows = await ReadRows(connection, existingDestination);
        var beforeIndex = await ReadIndex(connection);
        using var keyMetadata = JsonDocument.Parse(beforeIndex);
        Check(
            keyMetadata.RootElement.GetArrayLength() == 2
                && keyMetadata
                    .RootElement.EnumerateArray()
                    .All(row => row.GetProperty("fill_factor").GetByte() == (existingDestination ? 80 : 0)),
            "Legacy fixtures must exercise explicit and default fill-factor metadata."
        );
        var beforeDefaults = await ReadDefaults(connection);
        var beforeCollations = await ReadCollations(connection);
        CheckCollations(beforeCollations, existingDestination ? 3 : 2, "SQL_Latin1_General_CP1_CI_AS");
        await CreateAlerts(connection, account, otherAccount);

        await using (
            var statistic = new SqlCommand(
                "CREATE STATISTICS ST_TradingJobDispatch_Legacy_Fixture ON dbo.TradingJobDispatch(AlertKey);",
                connection
            )
        )
        {
            await statistic.ExecuteNonQueryAsync();
        }

        var rejected = false;
        try
        {
            await new SqlTradingJobDispatchStore(connectionString, account).Initialize(CancellationToken.None);
        }
        catch (SqlException exception)
            when (exception.Number == 50000 && exception.Message.Contains("custom statistics", StringComparison.Ordinal)
            )
        {
            rejected = true;
        }

        Check(rejected, "Custom target statistics must reject the migration explicitly.");
        Check(
            await HasDestination(connection) == existingDestination,
            "Failed migration cannot leave an added column."
        );
        Check(
            await ReadRows(connection, existingDestination) == beforeRows
                && await ReadIndex(connection) == beforeIndex
                && await ReadDefaults(connection) == beforeDefaults
                && await ReadCollations(connection) == beforeCollations,
            "Failure preserves every retained row, key, default and collation."
        );

        await using (
            var drop = new SqlCommand(
                "DROP STATISTICS dbo.TradingJobDispatch.ST_TradingJobDispatch_Legacy_Fixture;",
                connection
            )
        )
        {
            await drop.ExecuteNonQueryAsync();
        }

        var store = new SqlTradingJobDispatchStore(connectionString, account);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.Initialize(CancellationToken.None)));
        Check(await HasDestination(connection), "Successful migration provides a destination column.");
        CheckCollations(await ReadCollations(connection), 3, "Latin1_General_100_BIN2");
        Check(
            await ReadRows(connection, existingDestination) == beforeRows && await ReadIndex(connection) == beforeIndex,
            "Concurrent initialization preserves all original fields/accounts and supported primary-key settings."
        );
        Check(
            await ReadDestination(connection, account, "LegacyAlert")
                == (existingDestination ? "legacy-destination" : string.Empty),
            "Legacy destination bindings remain exact; missing bindings are not silently adopted."
        );
        if (existingDestination)
        {
            Check(
                await ReadDefaults(connection) == beforeDefaults,
                "Existing default names and expressions are retained."
            );
        }

        var migratedRows = await ReadRows(connection, true);
        var migratedDefaults = await ReadDefaults(connection);
        await store.Initialize(CancellationToken.None);
        Check(
            await ReadRows(connection, true) == migratedRows
                && await ReadDefaults(connection) == migratedDefaults
                && await ReadIndex(connection) == beforeIndex,
            "An already migrated table is idempotent without rewriting retained rows or metadata."
        );

        now = DateTimeOffset.UtcNow;
        var distinct =
            await store.Claim("legacyalert", "telegram", now, CancellationToken.None)
            ?? throw new InvalidOperationException("Case-distinct claim is missing.");
        Check(
            await store.MarkEnqueued("legacyalert", distinct, "case-job", CancellationToken.None)
                && (await store.Read("LegacyAlert", CancellationToken.None))?.JobId == "legacy-job"
                && (await store.Read("legacyalert", CancellationToken.None))?.JobId == "case-job",
            "Exact case pairs retain independent jobs and lease acknowledgements."
        );
        Check(
            !await store.MarkEnqueued("LegacyAlert", distinct, "wrong-case", CancellationToken.None)
                && !await store.MarkEnqueued("legacyalert", Guid.NewGuid(), "wrong-lease", CancellationToken.None),
            "A different alert identity or lease cannot acknowledge a dispatch."
        );
        await MakeEligible(connection, account, "legacyalert", now);
        Check(
            !await store.MarkEnqueued("legacyalert", distinct, "expired", CancellationToken.None),
            "An expired lease cannot acknowledge even while State is Claimed."
        );

        _ =
            await store.Claim("FreshCase", "telegram", now, CancellationToken.None)
            ?? throw new InvalidOperationException("Fresh destination claim is missing.");
        _ =
            await store.Claim("freshcase", "telegram", now, CancellationToken.None)
            ?? throw new InvalidOperationException("Second case-pair claim is missing.");
        await MakeEligible(connection, account, "FreshCase", now);
        var attemptsBeforeMismatch = (await store.Read("FreshCase", CancellationToken.None))?.Attempts;
        Check(
            await store.Claim("FreshCase", "Telegram", now, CancellationToken.None) is null
                && (await store.Read("FreshCase", CancellationToken.None))?.Attempts == attemptsBeforeMismatch
                && await store.Claim("FreshCase", "telegram", now, CancellationToken.None) is not null,
            "Only the exact destination claims an otherwise eligible retained row."
        );

        await MakeEligible(connection, account, "freshcase", now, 8);
        await MakeEligible(connection, account, "FreshCase", now, 2);
        Check(
            await store.Claim("freshcase", "telegram", now, CancellationToken.None) is null
                && await store.Claim("FreshCase", "telegram", now, CancellationToken.None) is not null
                && (await store.Read("freshcase", CancellationToken.None))?.Attempts == 8
                && (await store.Read("FreshCase", CancellationToken.None))?.Attempts == 3,
            "Attempt caps are independent of case-distinct keys, lease expiry and due time."
        );

        var other = new SqlTradingJobDispatchStore(connectionString, otherAccount);
        Check(
            await other.Read("LegacyAlert", CancellationToken.None) is null
                && (await other.Read("OtherAlert", CancellationToken.None))?.JobId == "other-job"
                && await store.Read("OtherAlert", CancellationToken.None) is null,
            "Other-account rows remain readable only through their exact partition."
        );
        Check(
            await store.CountBlocked("telegram", CancellationToken.None) == 1,
            "Blocked count includes exactly the capped canonical pending key, excluding healthy variants and other accounts."
        );

        Console.WriteLine(
            "Trading dispatch migration SQL checks passed: "
                + shape
                + ", rollback, retained rows/defaults/key, concurrent initialize, case identities, exact destination, leases and caps."
        );
    }

    private static string AccountKey() =>
        Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant();

    private static async Task SeedLegacy(
        SqlConnection connection,
        string account,
        string otherAccount,
        DateTimeOffset now,
        bool existingDestination
    )
    {
        var destination = existingDestination
            ? "DestinationKey nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL CONSTRAINT DF_LegacyDispatch_Destination DEFAULT N'legacy-destination',"
            : string.Empty;
        var primaryKey = existingDestination
            ? "CONSTRAINT PK_LegacyTradingJobDispatch PRIMARY KEY NONCLUSTERED(AccountKey,AlertKey) WITH(FILLFACTOR=80,PAD_INDEX=ON)"
            : "CONSTRAINT PK_LegacyTradingJobDispatch PRIMARY KEY CLUSTERED(AccountKey,AlertKey)";
        var sql =
            """
                CREATE TABLE dbo.TradingJobDispatch(
                    AccountKey char(64) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
                    AlertKey nvarchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
                """
            + destination
            + """
                    JobId nvarchar(128) NULL,LeaseToken uniqueidentifier NULL,LeaseUntil datetimeoffset NULL,
                    DueAt datetimeoffset NOT NULL,Attempts int NOT NULL,State varchar(20) NOT NULL,
                """
            + primaryKey
            + """
                    );
                INSERT dbo.TradingJobDispatch(AccountKey,AlertKey,JobId,LeaseToken,LeaseUntil,DueAt,Attempts,State)
                    VALUES(@account,N'LegacyAlert',N'legacy-job',@lease,DATEADD(minute,5,@now),@now,2,'Enqueued'),
                        (@other,N'OtherAlert',N'other-job',NULL,NULL,@now,1,'Enqueued');
                """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@other", SqlDbType.Char, 64).Value = otherAccount;
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@lease", SqlDbType.UniqueIdentifier).Value = Guid.Parse(
            "11111111-1111-1111-1111-111111111111"
        );
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateAlerts(SqlConnection connection, string account, string otherAccount)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.TradingAlerts',N'U') IS NULL CREATE TABLE dbo.TradingAlerts(
                AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                Id nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,Payload nvarchar(max) NOT NULL,
                PRIMARY KEY(AccountKey,Id));
            INSERT dbo.TradingAlerts(AccountKey,Id,Payload) VALUES
                (@account,N'LegacyAlert',N'{"status":"sent"}'),(@account,N'legacyalert',N'{"status":"pending"}'),
                (@account,N'FreshCase',N'{"status":"pending"}'),(@account,N'freshcase',N'{"status":"pending"}'),
                (@other,N'OtherAlert',N'{"status":"pending"}');
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@other", SqlDbType.Char, 64).Value = otherAccount;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MakeEligible(
        SqlConnection connection,
        string account,
        string alert,
        DateTimeOffset claimAt,
        int? attempts = null
    )
    {
        await using var command = new SqlCommand(
            """
            UPDATE dbo.TradingJobDispatch SET DueAt=DATEADD(day,-1,@claimAt),
                LeaseUntil=DATEADD(day,-1,@claimAt),State='Claimed',Attempts=COALESCE(@attempts,Attempts)
                WHERE AccountKey=@account AND AlertKey=@alert;
            """,
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alert;
        command.Parameters.Add("@claimAt", SqlDbType.DateTimeOffset).Value = claimAt;
        command.Parameters.Add("@attempts", SqlDbType.Int).Value = attempts.HasValue ? attempts.Value : DBNull.Value;
        Check(await command.ExecuteNonQueryAsync() == 1, "Eligibility fixture must select one exact dispatch row.");
    }

    private static async Task<bool> Exists(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.TradingJobDispatch',N'U') IS NULL THEN 0 ELSE 1 END;",
            connection
        );
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasDestination(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SELECT CASE WHEN COL_LENGTH(N'dbo.TradingJobDispatch',N'DestinationKey') IS NULL THEN 0 ELSE 1 END;",
            connection
        );
        return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadDestination(SqlConnection connection, string account, string alert)
    {
        await using var command = new SqlCommand(
            "SELECT DestinationKey FROM dbo.TradingJobDispatch WHERE AccountKey=@account AND AlertKey=@alert;",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alert;
        return await command.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("Dispatch row missing.");
    }

    private static Task<string> ReadRows(SqlConnection connection, bool includeDestination)
    {
        var sql =
            "SELECT (SELECT AccountKey,AlertKey,JobId,LeaseToken,LeaseUntil,DueAt,Attempts,State"
            + (includeDestination ? ",DestinationKey" : string.Empty)
            + " FROM dbo.TradingJobDispatch ORDER BY AccountKey,AlertKey FOR JSON PATH,INCLUDE_NULL_VALUES);";
        return ReadJson(connection, sql);
    }

    private static Task<string> ReadIndex(SqlConnection connection) =>
        ReadJson(
            connection,
            """
            SELECT (SELECT i.name,i.type_desc,i.fill_factor,i.is_padded,i.ignore_dup_key,
                i.allow_row_locks,i.allow_page_locks,s.no_recompute,d.name AS filegroup_name,
                p.data_compression_desc,ic.key_ordinal,c.name AS column_name
                FROM sys.indexes i INNER JOIN sys.stats s ON s.object_id=i.object_id AND s.stats_id=i.index_id
                INNER JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                INNER JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                INNER JOIN sys.data_spaces d ON d.data_space_id=i.data_space_id
                INNER JOIN sys.partitions p ON p.object_id=i.object_id AND p.index_id=i.index_id
                WHERE i.object_id=OBJECT_ID(N'dbo.TradingJobDispatch') AND i.is_primary_key=1
                ORDER BY ic.key_ordinal FOR JSON PATH);
            """
        );

    private static Task<string> ReadDefaults(SqlConnection connection) =>
        ReadJson(
            connection,
            """
            SELECT (SELECT dc.name,dc.definition,c.name AS column_name FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
                WHERE dc.parent_object_id=OBJECT_ID(N'dbo.TradingJobDispatch') ORDER BY c.name FOR JSON PATH);
            """
        );

    private static Task<string> ReadCollations(SqlConnection connection) =>
        ReadJson(
            connection,
            """
            SELECT (SELECT name,collation_name AS collation FROM sys.columns
                WHERE object_id=OBJECT_ID(N'dbo.TradingJobDispatch')
                    AND name IN(N'AccountKey',N'AlertKey',N'DestinationKey') ORDER BY name FOR JSON PATH);
            """
        );

    private static async Task<string> ReadJson(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("Fixture metadata is missing.");
    }

    private static void CheckCollations(string json, int expectedColumns, string expectedCollation)
    {
        using var document = JsonDocument.Parse(json);
        Check(document.RootElement.GetArrayLength() == expectedColumns, "Every expected identity column must exist.");
        foreach (var column in document.RootElement.EnumerateArray())
        {
            Check(
                column.GetProperty("collation").GetString() == expectedCollation,
                "Identity columns must use the exact expected collation."
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
