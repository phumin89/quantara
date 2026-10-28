using System.Data;
using Microsoft.Data.SqlClient;

namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed class SqlTradingJobDispatchStore(string connectionString, string accountKey)
{
    public async Task Initialize(CancellationToken cancellationToken)
    {
        if (accountKey.Length != 64)
        {
            throw new InvalidOperationException("Account key must be a 64-character identifier.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SET XACT_ABORT ON;
            BEGIN TRY
                BEGIN TRANSACTION;
                DECLARE @lock int;
                EXEC @lock=sys.sp_getapplock @Resource=N'Quantara.TradingJobDispatch.Schema',
                    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
                IF @lock<0 THROW 50000,'Trading dispatch schema is busy.',1;

                IF OBJECT_ID(N'dbo.TradingJobDispatch',N'U') IS NULL
                    CREATE TABLE dbo.TradingJobDispatch(
                        AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
                        AlertKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                        DestinationKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                        JobId nvarchar(128) NULL,LeaseToken uniqueidentifier NULL,
                        LeaseUntil datetimeoffset NULL,DueAt datetimeoffset NOT NULL,
                        Attempts int NOT NULL,State varchar(20) NOT NULL,PRIMARY KEY(AccountKey,AlertKey));
                ELSE
                BEGIN
                    IF COL_LENGTH(N'dbo.TradingJobDispatch',N'DestinationKey') IS NULL
                        ALTER TABLE dbo.TradingJobDispatch ADD DestinationKey nvarchar(200)
                            COLLATE Latin1_General_100_BIN2 NOT NULL
                            CONSTRAINT DF_TradingJobDispatch_DestinationKey DEFAULT N'';

                    DECLARE @table int=OBJECT_ID(N'dbo.TradingJobDispatch');
                    IF EXISTS(
                        SELECT 1 FROM (VALUES(N'AccountKey',TYPE_ID(N'char'),64),
                            (N'AlertKey',TYPE_ID(N'nvarchar'),400),
                            (N'DestinationKey',TYPE_ID(N'nvarchar'),400)) expected(Name,TypeId,Length)
                        LEFT JOIN sys.columns c ON c.object_id=@table AND c.name=expected.Name
                        WHERE c.column_id IS NULL OR c.user_type_id<>expected.TypeId
                            OR c.max_length<>expected.Length OR c.is_nullable<>0 OR c.is_computed<>0)
                        THROW 50000,'Unsupported trading dispatch identity columns; schema unchanged.',1;

                    IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=@table
                        AND name IN(N'AccountKey',N'AlertKey',N'DestinationKey')
                        AND collation_name<>N'Latin1_General_100_BIN2')
                    BEGIN
                        DECLARE @rows bigint;
                        SELECT @rows=COUNT_BIG(*) FROM dbo.TradingJobDispatch WITH(TABLOCKX,HOLDLOCK);
                        IF EXISTS(SELECT 1 FROM dbo.TradingJobDispatch
                            WHERE AccountKey COLLATE Latin1_General_100_BIN2 LIKE '%[^ -~]%')
                            THROW 50000,'Non-ASCII trading dispatch account binding; schema unchanged.',1;
                        DECLARE @key sysname,@constraint int,@index int,@kind tinyint,@filegroup sysname,
                            @compression nvarchar(60),@options nvarchar(max),@fillFactor tinyint;
                        SELECT @key=k.name,@constraint=k.object_id,@index=i.index_id,@kind=i.type,@filegroup=d.name,
                            @compression=p.data_compression_desc,@fillFactor=i.fill_factor,
                            @options=CONCAT(N'WITH(PAD_INDEX=',CASE i.is_padded WHEN 1 THEN N'ON' ELSE N'OFF' END,
                                CASE WHEN i.fill_factor=0 THEN N'' ELSE CONCAT(N',FILLFACTOR=',i.fill_factor) END,
                                N',IGNORE_DUP_KEY=',CASE i.ignore_dup_key WHEN 1 THEN N'ON' ELSE N'OFF' END,
                                N',STATISTICS_NORECOMPUTE=',CASE s.no_recompute WHEN 1 THEN N'ON' ELSE N'OFF' END,
                                N',ALLOW_ROW_LOCKS=',CASE i.allow_row_locks WHEN 1 THEN N'ON' ELSE N'OFF' END,
                                N',ALLOW_PAGE_LOCKS=',CASE i.allow_page_locks WHEN 1 THEN N'ON' ELSE N'OFF' END,
                                N',DATA_COMPRESSION=',p.data_compression_desc,
                                CASE WHEN JSON_VALUE((SELECT i.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                                    '$.optimize_for_sequential_key') IS NULL THEN N''
                                    ELSE CONCAT(N',OPTIMIZE_FOR_SEQUENTIAL_KEY=',
                                        CASE JSON_VALUE((SELECT i.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                                            '$.optimize_for_sequential_key') WHEN N'true' THEN N'ON' ELSE N'OFF' END) END,N')')
                        FROM sys.key_constraints k
                        INNER JOIN sys.indexes i ON i.object_id=k.parent_object_id AND i.index_id=k.unique_index_id
                        INNER JOIN sys.stats s ON s.object_id=i.object_id AND s.stats_id=i.index_id
                        INNER JOIN sys.data_spaces d ON d.data_space_id=i.data_space_id AND d.type='FG'
                        INNER JOIN sys.partitions p ON p.object_id=i.object_id AND p.index_id=i.index_id AND p.partition_number=1
                        WHERE k.parent_object_id=@table AND k.type='PK' AND i.is_disabled=0;
                        IF @key IS NULL OR @kind NOT IN(1,2) OR @compression NOT IN(N'NONE',N'ROW',N'PAGE')
                            OR (SELECT COUNT(*) FROM sys.partitions WHERE object_id=@table AND index_id=@index)<>1
                            OR (SELECT COUNT(*) FROM sys.index_columns WHERE object_id=@table AND index_id=@index AND key_ordinal>0)<>2
                            OR NOT EXISTS(SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
                                ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=@table
                                AND ic.index_id=@index AND ic.key_ordinal=1 AND c.name=N'AccountKey' AND ic.is_descending_key=0)
                            OR NOT EXISTS(SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
                                ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=@table
                                AND ic.index_id=@index AND ic.key_ordinal=2 AND c.name=N'AlertKey' AND ic.is_descending_key=0)
                            THROW 50000,'Unsupported trading dispatch primary key; schema unchanged.',1;

                        IF EXISTS(SELECT 1 FROM sys.stats s INNER JOIN sys.stats_columns sc
                            ON sc.object_id=s.object_id AND sc.stats_id=s.stats_id
                            INNER JOIN sys.columns c ON c.object_id=sc.object_id AND c.column_id=sc.column_id
                            WHERE s.object_id=@table AND s.user_created=1
                            AND c.name IN(N'AccountKey',N'AlertKey',N'DestinationKey'))
                            THROW 50000,'Trading dispatch identity has custom statistics; schema unchanged.',1;
                        IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE
                            (class=7 AND major_id=@table AND minor_id=@index)
                            OR (class=1 AND (major_id=@constraint OR major_id IN(
                                SELECT dc.object_id FROM sys.default_constraints dc INNER JOIN sys.columns c
                                    ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
                                WHERE dc.parent_object_id=@table AND c.name IN(N'AccountKey',N'AlertKey',N'DestinationKey')))))
                            THROW 50000,'Trading dispatch constraints have custom metadata; schema unchanged.',1;

                        DECLARE @dropDefaults nvarchar(max),@restoreDefaults nvarchar(max);
                        SELECT @dropDefaults=(SELECT N'ALTER TABLE dbo.TradingJobDispatch DROP CONSTRAINT '+QUOTENAME(dc.name)+N';'
                            FROM sys.default_constraints dc INNER JOIN sys.columns c
                                ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
                            WHERE dc.parent_object_id=@table AND c.name IN(N'AccountKey',N'AlertKey',N'DestinationKey')
                            FOR XML PATH(''),TYPE).value('.','nvarchar(max)');
                        SELECT @restoreDefaults=(SELECT N'ALTER TABLE dbo.TradingJobDispatch ADD CONSTRAINT '+QUOTENAME(dc.name)
                            +N' DEFAULT '+dc.definition+N' FOR '+QUOTENAME(c.name)+N';'
                            FROM sys.default_constraints dc INNER JOIN sys.columns c
                                ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
                            WHERE dc.parent_object_id=@table AND c.name IN(N'AccountKey',N'AlertKey',N'DestinationKey')
                            FOR XML PATH(''),TYPE).value('.','nvarchar(max)');
                        EXEC sys.sp_executesql @dropDefaults;
                        DECLARE @dropKey nvarchar(max)=N'ALTER TABLE dbo.TradingJobDispatch DROP CONSTRAINT '+QUOTENAME(@key)+N';';
                        EXEC sys.sp_executesql @dropKey;
                        EXEC sys.sp_executesql N'
                            ALTER TABLE dbo.TradingJobDispatch ALTER COLUMN AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
                            ALTER TABLE dbo.TradingJobDispatch ALTER COLUMN AlertKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL;
                            ALTER TABLE dbo.TradingJobDispatch ALTER COLUMN DestinationKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL;';
                        DECLARE @restoreKey nvarchar(max)=N'ALTER TABLE dbo.TradingJobDispatch ADD CONSTRAINT '+QUOTENAME(@key)
                            +N' PRIMARY KEY '+CASE @kind WHEN 1 THEN N'CLUSTERED' ELSE N'NONCLUSTERED' END
                            +N'(AccountKey,AlertKey) '+@options+N' ON '+QUOTENAME(@filegroup)+N';';
                        EXEC sys.sp_executesql @restoreKey;
                        IF NOT EXISTS(SELECT 1 FROM sys.key_constraints k INNER JOIN sys.indexes i
                            ON i.object_id=k.parent_object_id AND i.index_id=k.unique_index_id
                            WHERE k.parent_object_id=@table AND k.type='PK' AND k.name=@key
                                AND i.type=@kind AND i.fill_factor=@fillFactor)
                            THROW 50000,'Trading dispatch fill factor changed; schema unchanged.',1;
                        EXEC sys.sp_executesql @restoreDefaults;
                        IF (SELECT COUNT_BIG(*) FROM dbo.TradingJobDispatch)<>@rows
                            THROW 50000,'Trading dispatch rows changed during schema repair.',1;
                    END;
                END;
                COMMIT;
            END TRY
            BEGIN CATCH
                IF XACT_STATE()<>0 ROLLBACK;
                THROW;
            END CATCH;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid?> Claim(
        string alertKey,
        string destinationKey,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        ValidateAlertKey(alertKey);
        ValidateDestination(destinationKey);
        var leaseToken = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SET XACT_ABORT ON; BEGIN TRANSACTION; DECLARE @claimed bit=0; UPDATE dbo.TradingJobDispatch WITH(UPDLOCK,HOLDLOCK) SET LeaseToken=@lease,LeaseUntil=DATEADD(second,30,@now),DueAt=DATEADD(second,30,@now),Attempts=Attempts+1,State='Claimed' WHERE AccountKey=@account AND AlertKey=@alert AND DestinationKey=@destination AND Attempts<8 AND (LeaseUntil IS NULL OR LeaseUntil<=@now) AND DueAt<=@now; IF @@ROWCOUNT=1 SET @claimed=1; IF @claimed=0 BEGIN INSERT INTO dbo.TradingJobDispatch(AccountKey,AlertKey,DestinationKey,DueAt,Attempts,State,LeaseToken,LeaseUntil) SELECT @account,@alert,@destination,DATEADD(second,30,@now),1,'Claimed',@lease,DATEADD(second,30,@now) WHERE NOT EXISTS(SELECT 1 FROM dbo.TradingJobDispatch WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND AlertKey=@alert); IF @@ROWCOUNT=1 SET @claimed=1; END; COMMIT; SELECT @claimed;",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alertKey;
        command.Parameters.Add("@destination", SqlDbType.NVarChar, 200).Value = destinationKey;
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@lease", SqlDbType.UniqueIdentifier).Value = leaseToken;
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture
        )
            ? leaseToken
            : null;
    }

    public async Task<bool> MarkEnqueued(
        string alertKey,
        Guid leaseToken,
        string jobId,
        CancellationToken cancellationToken
    )
    {
        ValidateAlertKey(alertKey);
        if (leaseToken == Guid.Empty || string.IsNullOrWhiteSpace(jobId) || jobId.Length > 128)
        {
            throw new ArgumentException("A valid dispatch lease and job identifier are required.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.TradingJobDispatch SET JobId=@job,State='Enqueued',LeaseUntil=NULL WHERE AccountKey=@account AND AlertKey=@alert AND LeaseToken=@lease AND State='Claimed' AND LeaseUntil>SYSDATETIMEOFFSET();",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alertKey;
        command.Parameters.Add("@job", SqlDbType.NVarChar, 128).Value = jobId;
        command.Parameters.Add("@lease", SqlDbType.UniqueIdentifier).Value = leaseToken;
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<TradingJobDispatch?> Read(string alertKey, CancellationToken cancellationToken)
    {
        ValidateAlertKey(alertKey);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT JobId,DestinationKey,DueAt,Attempts FROM dbo.TradingJobDispatch WHERE AccountKey=@account AND AlertKey=@alert;",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = alertKey;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetDateTimeOffset(2),
                reader.GetInt32(3)
            )
            : null;
    }

    public async Task<int> CountBlocked(string destinationKey, CancellationToken cancellationToken)
    {
        ValidateDestination(destinationKey);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.TradingJobDispatch d INNER JOIN dbo.TradingAlerts a ON a.AccountKey=d.AccountKey COLLATE Latin1_General_100_BIN2 AND a.Id=d.AlertKey COLLATE Latin1_General_100_BIN2 WHERE d.AccountKey=@account AND JSON_VALUE(a.Payload,'$.status')='pending' AND (d.Attempts>=8 OR d.DestinationKey<>@destination);",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        command.Parameters.Add("@destination", SqlDbType.NVarChar, 200).Value = destinationKey;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static void ValidateAlertKey(string alertKey)
    {
        if (string.IsNullOrWhiteSpace(alertKey) || alertKey.Length > 200)
        {
            throw new ArgumentException("Alert key must contain 1 to 200 characters.", nameof(alertKey));
        }
    }

    private static void ValidateDestination(string destinationKey)
    {
        if (string.IsNullOrWhiteSpace(destinationKey) || destinationKey.Length > 200)
        {
            throw new ArgumentException("A destination binding is required.", nameof(destinationKey));
        }
    }
}
