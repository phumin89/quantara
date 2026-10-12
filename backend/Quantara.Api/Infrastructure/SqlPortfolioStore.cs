using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Api.Application;
using Quantara.Api.Domain;

namespace Quantara.Api.Infrastructure;

public sealed class SqlPortfolioStore(string connectionString, string accountKey) : IPortfolioStore
{
    public async Task Initialize(bool createLocalDatabase, CancellationToken cancellationToken)
    {
        if (createLocalDatabase)
        {
            var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var create = new SqlCommand("IF DB_ID(N'Quantara') IS NULL CREATE DATABASE [Quantara];", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var database = new SqlConnection(connectionString);
        await database.OpenAsync(cancellationToken);
        await using var schema = new SqlCommand("""
            IF OBJECT_ID(N'dbo.CryptoPortfolioLedger', N'U') IS NULL
            CREATE TABLE dbo.CryptoPortfolioLedger (
                AccountKey char(64) NOT NULL PRIMARY KEY,
                Payload nvarchar(max) NOT NULL CHECK (ISJSON(Payload) = 1),
                UpdatedAt datetimeoffset NOT NULL
            );
            """, database);
        await schema.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PortfolioLedger> Load(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var query = new SqlCommand("SELECT Payload FROM dbo.CryptoPortfolioLedger WHERE AccountKey = @account;", connection);
        query.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        var json = await query.ExecuteScalarAsync(cancellationToken);
        if (json is null) return new();
        return JsonSerializer.Deserialize<PortfolioLedger>((string)json)
            ?? throw new InvalidDataException("Stored portfolio could not be read.");
    }

    public async Task Save(PortfolioLedger ledger, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var save = new SqlCommand("""
            UPDATE dbo.CryptoPortfolioLedger WITH (UPDLOCK, SERIALIZABLE)
            SET Payload = @payload, UpdatedAt = SYSUTCDATETIME() WHERE AccountKey = @account;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.CryptoPortfolioLedger (AccountKey, Payload, UpdatedAt)
                VALUES (@account, @payload, SYSUTCDATETIME());
            """, connection, transaction);
        save.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        save.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(ledger);
        await save.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
