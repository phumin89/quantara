using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

// Reviewed retirement and its retained evidence share the runtime/mirror transaction and account revision claim.
public sealed class SqlManualSaleAllocationStore : IManualSaleAllocationStore
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string connectionString;

    private readonly string accountKey;

    private readonly TimeProvider time;

    public SqlManualSaleAllocationStore(string connectionString, string accountKey, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        if (
            accountKey.Length != 64
            || accountKey.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
        )
        {
            throw new ArgumentException("Manual-sale storage requires the canonical account hash.", nameof(accountKey));
        }

        this.connectionString = connectionString;
        this.accountKey = accountKey;
        this.time = time ?? TimeProvider.System;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.ManualSaleAllocationJournals',N'U') IS NULL CREATE TABLE dbo.ManualSaleAllocationJournals(AccountKey char(64) NOT NULL PRIMARY KEY,Revision bigint NOT NULL,Payload nvarchar(max) NOT NULL);",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ManualSaleAllocationJournal> Read(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Revision,Payload FROM dbo.ManualSaleAllocationJournals WHERE AccountKey=@account;",
            connection
        );
        this.Account(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? this.Decode(reader.GetInt64(0), reader.GetString(1))
            : new ManualSaleAllocationJournal(this.accountKey, 0, []);
    }

    public async Task<bool> Append(
        ManualSaleAllocationReceipt receipt,
        long expectedJournalRevision,
        long expectedTradingRevision,
        CancellationToken cancellationToken,
        TradingExternalSale? retirement = null
    )
    {
        if (expectedJournalRevision is < 0 or > 1024 || expectedTradingRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedJournalRevision));
        }

        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var runtime = new SqlCommand(
            "SELECT Revision,Payload FROM dbo.TradingRuntime WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account;",
            connection,
            transaction
        );
        this.Account(runtime);
        TradingState tradingState;
        await using (var reader = await runtime.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt64(0) != expectedTradingRevision)
            {
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            tradingState =
                JsonSerializer.Deserialize<TradingState>(reader.GetString(1), TradingJson.Options)
                ?? throw new InvalidDataException("Trading state is missing.");
            if (tradingState.Revision != expectedTradingRevision)
            {
                throw new InvalidDataException("Trading revision does not match its payload.");
            }
        }

        var tradingStore = new SqlTradingStore(this.connectionString, this.accountKey, this.time);
        tradingStore.Validate(tradingState);
        await using var find = new SqlCommand(
            "SELECT Revision,Payload FROM dbo.ManualSaleAllocationJournals WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account;",
            connection,
            transaction
        );
        this.Account(find);
        var current = new ManualSaleAllocationJournal(this.accountKey, 0, []);
        await using (var reader = await find.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                current = this.Decode(reader.GetInt64(0), reader.GetString(1));
            }
        }

        if (current.Revision != expectedJournalRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var retained = current.Receipts.SingleOrDefault(item =>
            item.Allocation.Lot.LotKey == receipt.Allocation.Lot.LotKey
            && item.Allocation.Execution.ExecutionId == receipt.Allocation.Execution.ExecutionId
        );
        if (retained is not null && (retirement is null || retained.Allocation != receipt.Allocation))
        {
            throw new InvalidDataException("Retained sale attribution conflicts with this claim.");
        }

        var next = current with
        {
            Revision = current.Revision + (retained is null ? 1 : 0),
            Receipts = retained is null ? current.Receipts.Append(receipt).ToArray() : current.Receipts,
        };
        next.Validate(this.accountKey);
        if (receipt.ReviewExpiresAt is { } expiresAt && this.time.GetUtcNow() >= expiresAt)
        {
            throw new InvalidOperationException("Manual-sale review expired before the durable audit claim.");
        }

        var payload = JsonSerializer.Serialize(next, SqlManualSaleAllocationStore.jsonOptions);
        if (payload.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Manual-sale journal exceeds the write bound; retained evidence was not changed."
            );
        }

        if (retained is null)
        {
            await using var update = new SqlCommand(
                current.Revision == 0
                    ? "INSERT INTO dbo.ManualSaleAllocationJournals(AccountKey,Revision,Payload) VALUES(@account,@revision,@payload);"
                    : "UPDATE dbo.ManualSaleAllocationJournals SET Revision=@revision,Payload=@payload WHERE AccountKey=@account AND Revision=@expected;",
                connection,
                transaction
            );
            this.Account(update);
            update.Parameters.Add("@revision", SqlDbType.BigInt).Value = next.Revision;
            update.Parameters.Add("@expected", SqlDbType.BigInt).Value = expectedJournalRevision;
            update.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = payload;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidDataException("Manual-sale audit did not persist exactly one account revision.");
            }
        }

        if (retirement is not null)
        {
            if (retirement.Allocation != receipt.Allocation || retirement.ReviewReference != receipt.ReviewReference)
            {
                throw new InvalidDataException("Inventory retirement is not bound to its exact reviewed evidence.");
            }

            var transitioned = TradingManualSaleInventory.Retire(
                tradingState,
                retirement,
                this.accountKey,
                this.time.GetUtcNow()
            );
            await tradingStore.Persist(connection, transaction, transitioned, tradingState, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private ManualSaleAllocationJournal Decode(long revision, string payload)
    {
        if (payload.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Manual-sale journal exceeds the read bound.");
        }

        var journal =
            JsonSerializer.Deserialize<ManualSaleAllocationJournal>(payload, SqlManualSaleAllocationStore.jsonOptions)
            ?? throw new InvalidDataException("Manual-sale journal is empty; retained evidence was not reset.");
        journal.Validate(this.accountKey);
        if (journal.Revision != revision)
        {
            throw new InvalidDataException("Manual-sale journal revision does not match its payload.");
        }

        return journal;
    }

    private void Account(SqlCommand command) =>
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = this.accountKey;
}
