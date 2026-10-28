using System.Data;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlTradingNotificationReceiptStore : ITradingNotificationReceiptStore
{
    private readonly string connectionString;

    private readonly string accountKey;

    private readonly string destinationKey;

    public SqlTradingNotificationReceiptStore(string connectionString, string accountKey, string destinationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationKey);
        if (accountKey.Length != 64 || destinationKey.Length > 200 || destinationKey != destinationKey.Trim())
        {
            throw new ArgumentException("Notification receipts require an exact account and destination identity.");
        }

        this.connectionString = connectionString;
        this.accountKey = accountKey;
        this.destinationKey = destinationKey;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.TradingNotificationReceipts',N'U') IS NULL CREATE TABLE dbo.TradingNotificationReceipts(AccountKey char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,DestinationKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,AlertKey nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,ClaimedAt datetimeoffset(7) NOT NULL,MessageId bigint NOT NULL CHECK(MessageId>0),SentAt datetimeoffset(7) NOT NULL,CHECK(SentAt>=ClaimedAt),PRIMARY KEY(AccountKey,DestinationKey,AlertKey,ClaimedAt));",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Save(TradingNotificationReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SET XACT_ABORT ON; BEGIN TRANSACTION; IF EXISTS(SELECT 1 FROM dbo.TradingNotificationReceipts WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND DestinationKey=@destination AND AlertKey=@alert AND ClaimedAt=@claim) BEGIN IF NOT EXISTS(SELECT 1 FROM dbo.TradingNotificationReceipts WHERE AccountKey=@account AND DestinationKey=@destination AND AlertKey=@alert AND ClaimedAt=@claim AND DATALENGTH(AccountKey)=DATALENGTH(@account) AND CONVERT(varbinary(max),AccountKey)=CONVERT(varbinary(max),@account) AND DATALENGTH(DestinationKey)=DATALENGTH(@destination) AND CONVERT(varbinary(max),DestinationKey)=CONVERT(varbinary(max),@destination) AND DATALENGTH(AlertKey)=DATALENGTH(@alert) AND CONVERT(varbinary(max),AlertKey)=CONVERT(varbinary(max),@alert) AND MessageId=@message AND SentAt=@sent) THROW 51000,'Notification receipt conflicts with its retained acknowledgement.',1; END ELSE INSERT INTO dbo.TradingNotificationReceipts(AccountKey,DestinationKey,AlertKey,ClaimedAt,MessageId,SentAt) VALUES(@account,@destination,@alert,@claim,@message,@sent); COMMIT;",
            connection
        );
        this.Identity(command, receipt.Key, receipt.ClaimedAt);
        command.Parameters.Add("@message", SqlDbType.BigInt).Value = receipt.MessageId;
        command.Parameters.Add("@sent", SqlDbType.DateTimeOffset).Value = receipt.SentAt;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TradingNotificationReceipt?> Read(
        string key,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT AccountKey,DestinationKey,AlertKey,ClaimedAt,MessageId,SentAt FROM dbo.TradingNotificationReceipts WHERE AccountKey=@account AND DestinationKey=@destination AND AlertKey=@alert AND ClaimedAt=@claim;",
            connection
        );
        this.Identity(command, key, claimedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (
            !await reader.ReadAsync(cancellationToken)
            || !StringComparer.Ordinal.Equals(reader.GetString(0), this.accountKey)
            || !StringComparer.Ordinal.Equals(reader.GetString(1), this.destinationKey)
            || !StringComparer.Ordinal.Equals(reader.GetString(2), key)
            || reader.GetDateTimeOffset(3) != claimedAt
        )
        {
            return null;
        }

        return new(reader.GetString(2), reader.GetDateTimeOffset(3), reader.GetInt64(4), reader.GetDateTimeOffset(5));
    }

    private void Identity(SqlCommand command, string key, DateTimeOffset claimedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 200 || key != key.Trim() || claimedAt == default)
        {
            throw new ArgumentException("Notification receipt identity is invalid.");
        }

        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = this.accountKey;
        command.Parameters.Add("@destination", SqlDbType.NVarChar, 200).Value = this.destinationKey;
        command.Parameters.Add("@alert", SqlDbType.NVarChar, 200).Value = key;
        command.Parameters.Add("@claim", SqlDbType.DateTimeOffset).Value = claimedAt;
    }
}
