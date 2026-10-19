using System.Data;
using Microsoft.Data.SqlClient;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class SqlResearchBudget(string connectionString) : IResearchBudget
{
    public async Task<ResearchBudgetDecision> TryClaim(
        string accountKey,
        bool hasHeldPosition,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (accountKey.Length != 64 || !accountKey.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Invalid research budget account.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText =
                "IF OBJECT_ID(N'dbo.ResearchAttempts',N'U') IS NULL BEGIN CREATE TABLE dbo.ResearchAttempts(Id uniqueidentifier NOT NULL PRIMARY KEY,AccountKey varchar(64) NOT NULL,ClaimedAt datetimeoffset(7) NOT NULL,PositionReview bit NOT NULL); CREATE INDEX IX_ResearchAttempts_AccountTime ON dbo.ResearchAttempts(AccountKey,ClaimedAt); END";
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var history = new List<ResearchBudgetAttempt>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                "SELECT ClaimedAt, PositionReview FROM dbo.ResearchAttempts WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND ClaimedAt>=@from ORDER BY ClaimedAt;";
            read.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
            read.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = now.AddHours(-1);
            await using var rows = await read.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                history.Add(new ResearchBudgetAttempt(rows.GetFieldValue<DateTimeOffset>(0), rows.GetBoolean(1)));
            }
        }

        var decision = ResearchBudgetRules.Decide(history, hasHeldPosition, now);
        if (decision.Allowed)
        {
            await using var claim = connection.CreateCommand();
            claim.Transaction = transaction;
            claim.CommandText =
                "INSERT dbo.ResearchAttempts(Id,AccountKey,ClaimedAt,PositionReview) VALUES(@id,@account,@at,@position);";
            claim.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            claim.Parameters.Add("@account", SqlDbType.VarChar, 64).Value = accountKey;
            claim.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = now;
            claim.Parameters.Add("@position", SqlDbType.Bit).Value = hasHeldPosition;
            await claim.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return decision;
    }
}
