using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlPortfolioRepository(string connectionString, string accountKey) : IPortfolioRepository
{
    public async Task Initialize(bool createLocalDatabase, CancellationToken cancellationToken)
    {
        if (createLocalDatabase)
        {
            var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString); await connection.OpenAsync(cancellationToken);
            await using var create = new SqlCommand("IF DB_ID(N'Quantara') IS NULL CREATE DATABASE [Quantara];", connection); await create.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var database = new SqlConnection(connectionString); await database.OpenAsync(cancellationToken);
        using var migration = typeof(SqlPortfolioRepository).Assembly.GetManifestResourceStream("Portfolio.Infrastructure.Database.Migrations.001_CreateCryptoPortfolioLedger.sql")
            ?? throw new InvalidOperationException("Portfolio schema migration is missing.");
        using var reader = new StreamReader(migration);
        await using var schema = new SqlCommand(await reader.ReadToEndAsync(cancellationToken), database);
        await schema.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<PortfolioLedger> Load(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var query = new SqlCommand("SELECT Payload FROM dbo.CryptoPortfolioLedger WHERE AccountKey = @account;", connection); query.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;
        var json = await query.ExecuteScalarAsync(cancellationToken); return json is null ? new() : JsonSerializer.Deserialize<PortfolioLedger>((string)json) ?? throw new InvalidDataException("Stored portfolio could not be read.");
    }
    public async Task Save(PortfolioLedger ledger, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var save = new SqlCommand("UPDATE dbo.CryptoPortfolioLedger WITH (UPDLOCK, SERIALIZABLE) SET Payload = @payload, UpdatedAt = SYSUTCDATETIME() WHERE AccountKey = @account; IF @@ROWCOUNT = 0 INSERT INTO dbo.CryptoPortfolioLedger (AccountKey, Payload, UpdatedAt) VALUES (@account, @payload, SYSUTCDATETIME());", connection, transaction);
        save.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey; save.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(ledger); await save.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
}
