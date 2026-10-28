using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlOrderStore(string connectionString, string accountKey) : IOrderStore
{
    private static readonly JsonSerializerOptions jsonOptions = PortfolioJson.CreateOptions();

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "IF OBJECT_ID(N'dbo.OrderProposals', N'U') IS NULL BEGIN CREATE TABLE dbo.OrderProposals (AccountKey char(64) NOT NULL, Id uniqueidentifier NOT NULL, State varchar(20) NOT NULL, CreatedAt datetimeoffset NOT NULL, ExpiresAt datetimeoffset NOT NULL, Payload nvarchar(max) NOT NULL, CONSTRAINT PK_OrderProposals PRIMARY KEY(AccountKey, Id)); CREATE INDEX IX_OrderProposals_AccountState ON dbo.OrderProposals(AccountKey, State); END;",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderProposal>> List(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT TOP (50) Id, Payload, State FROM dbo.OrderProposals WHERE AccountKey=@account ORDER BY CASE WHEN State IN ('Submitting','Unknown') THEN 0 WHEN State IN ('Submitted','PartiallyFilled') THEN 1 ELSE 2 END, CreatedAt DESC, Id;",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        var result = new List<OrderProposal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(SqlOrderStore.Read(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return result;
    }

    public async Task<OrderProposal?> Find(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, Payload, State FROM dbo.OrderProposals WHERE AccountKey=@account AND Id=@id;",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? SqlOrderStore.Read(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public async Task<IReadOnlyList<OrderProposal>> Exposure(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id,Payload,State FROM dbo.OrderProposals WHERE AccountKey=@account AND (State IN ('Submitting','Unknown','Submitted','PartiallyFilled') OR State='Pending' AND ExpiresAt>@now);",
            connection
        );
        Account(command, accountKey);
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        var result = new List<OrderProposal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Read(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return result;
    }

    public async Task Create(OrderProposal proposal, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "INSERT INTO dbo.OrderProposals(AccountKey,Id,State,CreatedAt,ExpiresAt,Payload) VALUES(@account,@id,'Pending',@created,@expires,@payload);",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = proposal.Id;
        command.Parameters.Add("@created", SqlDbType.DateTimeOffset).Value = proposal.CreatedAt;
        command.Parameters.Add("@expires", SqlDbType.DateTimeOffset).Value = proposal.ExpiresAt;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            proposal,
            SqlOrderStore.jsonOptions
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> Claim(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        // One in-flight or uncertain submission per account; atomic across tabs and API hosts.
        await using var command = new SqlCommand(
            """
            DECLARE @lockResult int;
            EXEC @lockResult=sp_getapplock @Resource=@account,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
            IF @lockResult<0 THROW 50000,'Order account is busy.',1;
            UPDATE dbo.OrderProposals WITH (UPDLOCK,HOLDLOCK) SET State='Submitting'
            WHERE AccountKey=@account AND Id=@id AND State='Pending' AND ExpiresAt>@now
            AND NOT EXISTS(SELECT 1 FROM dbo.OrderProposals WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND State IN ('Submitting','Unknown'))
            AND NOT EXISTS(SELECT 1 FROM dbo.TradingRuntime WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND JSON_VALUE(Payload,'$.mode')='autoLive')
            AND (JSON_VALUE(dbo.OrderProposals.Payload,'$.Sizing.PolicyHash') IS NULL OR EXISTS(SELECT 1 FROM dbo.TradingRuntime WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND JSON_VALUE(Payload,'$.mode')='manualReview'))
            AND EXISTS(SELECT 1 FROM dbo.RiskSettings WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND ((Profile='Disabled' AND JSON_VALUE(dbo.OrderProposals.Payload,'$.Sizing.PolicyHash') IS NULL) OR PolicyHash=JSON_VALUE(dbo.OrderProposals.Payload,'$.Sizing.PolicyHash')));
            """,
            connection,
            transaction
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        var claimed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    public async Task Finish(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    )
    {
        var proposal = await this.Find(id, cancellationToken) ?? throw new InvalidOperationException("Order missing.");
        var updated = proposal with { State = state, BrokerOrderId = brokerOrderId, Message = message };
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.OrderProposals SET State=@state,Payload=@payload WHERE AccountKey=@account AND Id=@id AND State='Submitting';",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = state.ToString();
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            updated,
            SqlOrderStore.jsonOptions
        );
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Order outcome could not be stored. Check Bitkub; do not resubmit.");
        }
    }

    public async Task Reject(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.OrderProposals SET State='Rejected' WHERE AccountKey=@account AND Id=@id AND State='Pending';",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task UpdateObserved(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    ) => this.FinishObserved(id, state, brokerOrderId, message, cancellationToken);

    private async Task FinishObserved(
        Guid id,
        OrderState state,
        string? brokerOrderId,
        string? message,
        CancellationToken cancellationToken
    )
    {
        var proposal = await this.Find(id, cancellationToken) ?? throw new InvalidOperationException("Order missing.");
        if (
            proposal.State
            is not (OrderState.Submitting or OrderState.Unknown or OrderState.Submitted or OrderState.PartiallyFilled)
        )
        {
            throw new InvalidOperationException("Only submitted or uncertain orders can be reconciled.");
        }

        var updated = proposal with
        {
            State =
                proposal.State == OrderState.PartiallyFilled && state == OrderState.Submitted
                    ? OrderState.PartiallyFilled
                    : state,
            BrokerOrderId = brokerOrderId ?? proposal.BrokerOrderId,
            Message = message,
        };
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "UPDATE dbo.OrderProposals SET State=@state,Payload=@payload WHERE AccountKey=@account AND Id=@id AND State=@expected AND State IN ('Submitting','Unknown','Submitted','PartiallyFilled');",
            connection
        );
        SqlOrderStore.Account(command, accountKey);
        command.Parameters.Add("@expected", SqlDbType.VarChar, 20).Value = proposal.State.ToString();
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@state", SqlDbType.VarChar, 20).Value = updated.State.ToString();
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            updated,
            jsonOptions
        );
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Order outcome changed; reload before retrying.");
        }
    }

    private static OrderProposal Read(Guid id, string payload, string state)
    {
        var proposal =
            JsonSerializer.Deserialize<OrderProposal>(payload, SqlOrderStore.jsonOptions)
            ?? throw new InvalidDataException("Invalid stored order.");
        if (proposal.Id != id)
        {
            throw new InvalidDataException("Stored order identity does not match its row.");
        }

        return proposal with
        {
            State = Enum.Parse<OrderState>(state),
        };
    }

    private static void Account(SqlCommand command, string value)
    {
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = value;
    }
}
