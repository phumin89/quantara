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
            "IF OBJECT_ID(N'dbo.TradingJobDispatch',N'U') IS NULL CREATE TABLE dbo.TradingJobDispatch(AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,AlertKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,DestinationKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,JobId nvarchar(128) NULL,LeaseToken uniqueidentifier NULL,LeaseUntil datetimeoffset NULL,DueAt datetimeoffset NOT NULL,Attempts int NOT NULL,State varchar(20) NOT NULL,PRIMARY KEY(AccountKey,AlertKey)); ELSE IF COL_LENGTH(N'dbo.TradingJobDispatch',N'DestinationKey') IS NULL ALTER TABLE dbo.TradingJobDispatch ADD DestinationKey nvarchar(200) NOT NULL CONSTRAINT DF_TradingJobDispatch_DestinationKey DEFAULT N'';",
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
