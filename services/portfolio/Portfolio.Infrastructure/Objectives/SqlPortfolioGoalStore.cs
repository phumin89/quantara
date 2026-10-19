using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlPortfolioGoalStore(string connectionString, string accountKey) : IPortfolioGoalStore
{
    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.PortfolioGoalObservations',N'U') IS NULL CREATE TABLE dbo.PortfolioGoalObservations(AccountKey varchar(64) NOT NULL,ObjectiveId varchar(64) NOT NULL,ObservedAt datetimeoffset(7) NOT NULL,Payload nvarchar(max) NOT NULL,CONSTRAINT PK_PortfolioGoalObservations PRIMARY KEY(AccountKey,ObjectiveId,ObservedAt));",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Save(PortfolioGoalCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (checkpoint.AccountKey != accountKey || checkpoint.ObjectiveId != PortfolioObjective.Id)
        {
            throw new InvalidDataException("Goal checkpoint scope mismatch.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "INSERT dbo.PortfolioGoalObservations(AccountKey,ObjectiveId,ObservedAt,Payload) SELECT @account,@goal,@at,@payload WHERE NOT EXISTS(SELECT 1 FROM dbo.PortfolioGoalObservations WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND ObjectiveId=@goal AND ObservedAt=@at);",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
        command.Parameters.Add("@goal", SqlDbType.VarChar, 64).Value = PortfolioObjective.Id;
        command.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = checkpoint.ObservedAt;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(checkpoint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PortfolioGoalCheckpoint?> Read(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT TOP(1) Payload FROM dbo.PortfolioGoalObservations WHERE AccountKey=@account AND ObjectiveId=@goal AND ObservedAt<=@at ORDER BY ObservedAt DESC;",
            connection
        );
        command.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
        command.Parameters.Add("@goal", SqlDbType.VarChar, 64).Value = PortfolioObjective.Id;
        command.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = cutoff;
        return await command.ExecuteScalarAsync(cancellationToken) is string payload
            ? JsonSerializer.Deserialize<PortfolioGoalCheckpoint>(payload)
            : null;
    }
}
