using System.Data;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlOrderNotificationStore(string connectionString, string accountKey) : IOrderNotificationStore
{
    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.OrderNotifications',N'U') IS NULL CREATE TABLE dbo.OrderNotifications(AccountKey char(64) NOT NULL,OrderId uniqueidentifier NOT NULL,Token char(32) NOT NULL,UserId bigint NOT NULL,ChatId bigint NOT NULL,MessageId bigint NULL,State varchar(20) NOT NULL,Action varchar(10) NULL,ReviewedAt datetimeoffset NULL,PRIMARY KEY(AccountKey,OrderId),UNIQUE(Token)); IF OBJECT_ID(N'dbo.TelegramCursors',N'U') IS NULL CREATE TABLE dbo.TelegramCursors(AccountKey char(64) NOT NULL PRIMARY KEY,OffsetValue bigint NOT NULL);",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OrderNotification?> Reserve(
        Guid orderId,
        long userId,
        long chatId,
        CancellationToken cancellationToken
    )
    {
        var token = Guid.NewGuid().ToString("N");
        var changed = await this.Execute(
            "INSERT INTO dbo.OrderNotifications(AccountKey,OrderId,Token,UserId,ChatId,State) SELECT @account,@order,@token,@user,@chat,'Sending' WHERE NOT EXISTS(SELECT 1 FROM dbo.OrderNotifications WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND OrderId=@order);",
            orderId,
            token,
            userId,
            chatId,
            null,
            null,
            cancellationToken
        );
        return changed == 1 ? new(orderId, token, userId, chatId, null, OrderNotificationState.Sending) : null;
    }

    public async Task Sent(string token, long messageId, CancellationToken cancellationToken) =>
        await this.Execute(
            "UPDATE dbo.OrderNotifications SET State='Sent',MessageId=@message WHERE AccountKey=@account AND Token=@token AND State='Sending';",
            null,
            token,
            null,
            null,
            messageId,
            null,
            cancellationToken
        );

    public async Task<bool> ClaimReview(string token, string action, CancellationToken cancellationToken) =>
        await this.Execute(
            "UPDATE dbo.OrderNotifications SET State='Reviewed',Action=@action,ReviewedAt=SYSDATETIMEOFFSET() WHERE AccountKey=@account AND Token=@token AND State='Sent';",
            null,
            token,
            null,
            null,
            null,
            action,
            cancellationToken
        ) == 1;

    public async Task<OrderNotification?> Find(string token, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT OrderId,UserId,ChatId,MessageId,State FROM dbo.OrderNotifications WHERE AccountKey=@account AND Token=@token;",
            connection
        );
        SqlOrderNotificationStore.Account(command, accountKey);
        command.Parameters.Add("@token", SqlDbType.Char, 32).Value = token;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(
                reader.GetGuid(0),
                token,
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                Enum.Parse<OrderNotificationState>(reader.GetString(4))
            )
            : null;
    }

    public async Task<long> Cursor(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT OffsetValue FROM dbo.TelegramCursors WHERE AccountKey=@account;",
            connection
        );
        SqlOrderNotificationStore.Account(command, accountKey);
        return await command.ExecuteScalarAsync(cancellationToken) is long value ? value : 0;
    }

    public async Task Advance(long offset, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SET XACT_ABORT ON; BEGIN TRANSACTION; UPDATE dbo.TelegramCursors WITH(UPDLOCK,HOLDLOCK) SET OffsetValue=CASE WHEN OffsetValue<@offset THEN @offset ELSE OffsetValue END WHERE AccountKey=@account; IF @@ROWCOUNT=0 INSERT INTO dbo.TelegramCursors(AccountKey,OffsetValue) VALUES(@account,@offset); COMMIT;",
            connection
        );
        SqlOrderNotificationStore.Account(command, accountKey);
        command.Parameters.Add("@offset", SqlDbType.BigInt).Value = offset;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> Execute(
        string sql,
        Guid? orderId,
        string token,
        long? userId,
        long? chatId,
        long? messageId,
        string? action,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        SqlOrderNotificationStore.Account(command, accountKey);
        command.Parameters.Add("@token", SqlDbType.Char, 32).Value = token;
        command.Parameters.Add("@order", SqlDbType.UniqueIdentifier).Value = (object?)orderId ?? DBNull.Value;
        command.Parameters.Add("@user", SqlDbType.BigInt).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.Add("@chat", SqlDbType.BigInt).Value = (object?)chatId ?? DBNull.Value;
        command.Parameters.Add("@message", SqlDbType.BigInt).Value = (object?)messageId ?? DBNull.Value;
        command.Parameters.Add("@action", SqlDbType.VarChar, 10).Value = (object?)action ?? DBNull.Value;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Account(SqlCommand command, string account) =>
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = account;
}
