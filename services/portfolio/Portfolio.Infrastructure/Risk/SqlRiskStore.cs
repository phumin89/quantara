using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Infrastructure.Risk;

public sealed class SqlRiskStore(string connectionString, string accountKey) : IRiskStore
{
    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = await this.Open(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID(N'dbo.RiskSettings',N'U') IS NULL CREATE TABLE dbo.RiskSettings(AccountKey char(64) NOT NULL PRIMARY KEY,PolicyHash char(64) NOT NULL,Profile varchar(12) NOT NULL,Payload nvarchar(max) NOT NULL);
            IF OBJECT_ID(N'dbo.CapitalSnapshots',N'U') IS NULL BEGIN CREATE TABLE dbo.CapitalSnapshots(AccountKey char(64) NOT NULL,Id uniqueidentifier NOT NULL,ObservedAt datetimeoffset NOT NULL,Payload nvarchar(max) NOT NULL,Performance nvarchar(max) NULL,CONSTRAINT PK_CapitalSnapshots PRIMARY KEY(AccountKey,Id)); CREATE INDEX IX_CapitalSnapshots_AccountTime ON dbo.CapitalSnapshots(AccountKey,ObservedAt DESC); END;
            IF OBJECT_ID(N'dbo.CapitalCashFlows',N'U') IS NULL CREATE TABLE dbo.CapitalCashFlows(AccountKey char(64) NOT NULL,Id nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,At datetimeoffset NOT NULL,Payload nvarchar(max) NOT NULL,CONSTRAINT PK_CapitalCashFlows PRIMARY KEY(AccountKey,Id));
            IF OBJECT_ID(N'dbo.OrderSizingSnapshots',N'U') IS NULL CREATE TABLE dbo.OrderSizingSnapshots(AccountKey char(64) NOT NULL,Id uniqueidentifier NOT NULL,OrderId uniqueidentifier NOT NULL,ObservedAt datetimeoffset NOT NULL,Payload nvarchar(max) NOT NULL,CONSTRAINT PK_OrderSizingSnapshots PRIMARY KEY(AccountKey,Id));
            IF OBJECT_ID(N'dbo.RiskOrderFills',N'U') IS NULL CREATE TABLE dbo.RiskOrderFills(AccountKey char(64) NOT NULL,OrderId uniqueidentifier NOT NULL,TransactionId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,Payload nvarchar(max) NOT NULL,CONSTRAINT PK_RiskOrderFills PRIMARY KEY(AccountKey,OrderId,TransactionId));
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
        var initial = RiskPolicyHash.Default();
        await using var seed = new SqlCommand(
            "IF NOT EXISTS(SELECT 1 FROM dbo.RiskSettings WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account) INSERT dbo.RiskSettings(AccountKey,PolicyHash,Profile,Payload) VALUES(@account,@hash,'Disabled',@payload);",
            connection
        );
        this.Account(seed);
        seed.Parameters.Add("@hash", SqlDbType.Char, 64).Value = initial.PolicyHash;
        Payload(seed, "@payload", initial.Policy);
        await seed.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RiskPolicyState> ReadPolicy(CancellationToken cancellationToken)
    {
        await using var connection = await this.Open(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT PolicyHash,Payload FROM dbo.RiskSettings WHERE AccountKey=@account;",
            connection
        );
        this.Account(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Risk settings are unavailable.");
        }

        var policy = Read<RiskPolicy>(reader.GetString(1));
        policy.Validate();
        var hash = reader.GetString(0);
        if (hash != RiskPolicyHash.Compute(policy))
        {
            throw new InvalidDataException("Risk settings do not match their hash.");
        }

        return new(hash, policy);
    }

    public async Task Configure(RiskPolicy policy, string expectedHash, CancellationToken cancellationToken)
    {
        policy.Validate();
        await using var connection = await this.Open(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var command = new SqlCommand(
            """
            DECLARE @lockResult int;
            EXEC @lockResult=sp_getapplock @Resource=@account,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
            IF @lockResult<0 THROW 50000,'Risk account is busy.',1;
            IF EXISTS(SELECT 1 FROM dbo.OrderProposals WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND State IN ('Submitting','Unknown')) THROW 50000,'Reconcile in-flight orders before changing risk policy.',1;
            UPDATE dbo.RiskSettings SET PolicyHash=@hash,Profile=@profile,Payload=@payload WHERE AccountKey=@account AND PolicyHash=@expected;
            """,
            connection,
            transaction
        );
        this.Account(command);
        command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = RiskPolicyHash.Compute(policy);
        command.Parameters.Add("@profile", SqlDbType.VarChar, 12).Value = policy.Profile.ToString();
        command.Parameters.Add("@expected", SqlDbType.Char, 64).Value = expectedHash;
        Payload(command, "@payload", policy);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Risk policy changed. Reload before saving.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CapitalSnapshot?> ReadCapital(CancellationToken cancellationToken) =>
        await this.ReadLatest<CapitalSnapshot>("Payload", cancellationToken);

    public async Task<RiskPerformance?> ReadPerformance(CancellationToken cancellationToken) =>
        await this.ReadLatest<RiskPerformance>("Performance", cancellationToken);

    public async Task Observe(
        CapitalSnapshot snapshot,
        IReadOnlyList<CapitalCashFlow> cashFlows,
        DateTimeOffset coverageFrom,
        CancellationToken cancellationToken
    )
    {
        if (snapshot.AccountKey != accountKey)
        {
            throw new InvalidDataException("Capital snapshot belongs to another account.");
        }

        await using var connection = await this.Open(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        foreach (var flow in CapitalFlowEvidence.Unique(cashFlows, snapshot.ObservedAt))
        {
            if (
                flow.Id.Length is < 1 or > 256
                || flow.Quantity <= 0
                || flow.FeeQuantity < 0
                || flow.At > snapshot.ObservedAt
            )
            {
                throw new InvalidDataException("Invalid external cash flow.");
            }

            await using var insert = new SqlCommand(
                """
                IF EXISTS(SELECT 1 FROM dbo.CapitalCashFlows WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND Id=@id AND Payload COLLATE Latin1_General_100_BIN2<>@payload COLLATE Latin1_General_100_BIN2) THROW 50000,'Conflicting external cash flow.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.CapitalCashFlows WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND Id=@id) INSERT dbo.CapitalCashFlows(AccountKey,Id,At,Payload) VALUES(@account,@id,@at,@payload);
                """,
                connection,
                transaction
            );
            this.Account(insert);
            insert.Parameters.Add("@id", SqlDbType.NVarChar, 256).Value = flow.Id;
            insert.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = flow.At;
            Payload(insert, "@payload", flow);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        CapitalSnapshot? previous = null;
        await using (
            var read = new SqlCommand(
                "SELECT TOP(1) Payload FROM dbo.CapitalSnapshots WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account ORDER BY ObservedAt DESC,Id;",
                connection,
                transaction
            )
        )
        {
            this.Account(read);
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value is string text)
            {
                previous = Read<CapitalSnapshot>(text);
            }
        }

        RiskPerformance? performance = null;
        if (previous is not null && previous.ObservedAt < snapshot.ObservedAt)
        {
            var retainedFlows = new List<CapitalCashFlow>();
            await using var read = new SqlCommand(
                "SELECT Payload FROM dbo.CapitalCashFlows WHERE AccountKey=@account AND At>@from AND At<=@to;",
                connection,
                transaction
            );
            this.Account(read);
            read.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = previous.ObservedAt;
            read.Parameters.Add("@to", SqlDbType.DateTimeOffset).Value = snapshot.ObservedAt;
            await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    retainedFlows.Add(Read<CapitalCashFlow>(reader.GetString(0)));
                }
            }

            performance = RiskPerformanceCalculator.Calculate(previous, snapshot, retainedFlows, coverageFrom);
        }

        await using var save = new SqlCommand(
            "INSERT dbo.CapitalSnapshots(AccountKey,Id,ObservedAt,Payload,Performance) VALUES(@account,@id,@at,@payload,@performance);",
            connection,
            transaction
        );
        this.Account(save);
        save.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
        save.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = snapshot.ObservedAt;
        Payload(save, "@payload", snapshot);
        save.Parameters.Add("@performance", SqlDbType.NVarChar, -1).Value = performance is null
            ? DBNull.Value
            : JsonSerializer.Serialize(performance, RiskJson.Options);
        await save.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordSizing(Guid orderId, RiskSizingSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.AccountKey != accountKey || snapshot.Capital.AccountKey != accountKey)
        {
            throw new InvalidDataException("Sizing audit belongs to another account.");
        }

        await using var connection = await this.Open(cancellationToken);
        await using var command = new SqlCommand(
            "INSERT dbo.OrderSizingSnapshots(AccountKey,Id,OrderId,ObservedAt,Payload) VALUES(@account,@id,@order,@at,@payload);",
            connection
        );
        this.Account(command);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = snapshot.Id;
        command.Parameters.Add("@order", SqlDbType.UniqueIdentifier).Value = orderId;
        command.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = snapshot.Capital.ObservedAt;
        Payload(command, "@payload", snapshot);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordFills(
        Guid orderId,
        IReadOnlyList<RiskVerifiedFill> fills,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await this.Open(cancellationToken);
        foreach (var fill in fills)
        {
            if (
                fill.TransactionId.Length is < 1 or > 128
                || fill.Quantity <= 0
                || fill.Price <= 0
                || fill.FeeThb < 0
                || fill.FeeCreditThb < 0
                || fill.FeeCreditThb > fill.FeeThb
            )
            {
                throw new InvalidDataException("Invalid verified risk fill.");
            }

            await using var command = new SqlCommand(
                """
                IF EXISTS(SELECT 1 FROM dbo.RiskOrderFills WHERE AccountKey=@account AND OrderId=@order AND TransactionId=@id AND Payload COLLATE Latin1_General_100_BIN2<>@payload COLLATE Latin1_General_100_BIN2) THROW 50000,'Conflicting verified fill.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.RiskOrderFills WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND OrderId=@order AND TransactionId=@id) INSERT dbo.RiskOrderFills(AccountKey,OrderId,TransactionId,Payload) VALUES(@account,@order,@id,@payload);
                """,
                connection
            );
            this.Account(command);
            command.Parameters.Add("@order", SqlDbType.UniqueIdentifier).Value = orderId;
            command.Parameters.Add("@id", SqlDbType.NVarChar, 128).Value = fill.TransactionId;
            Payload(command, "@payload", fill);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<T?> ReadLatest<T>(string column, CancellationToken cancellationToken)
        where T : class
    {
        await using var connection = await this.Open(cancellationToken);
        var query =
            column == "Performance"
                ? "SELECT TOP(1) Performance FROM dbo.CapitalSnapshots WHERE AccountKey=@account ORDER BY ObservedAt DESC,Id;"
                : "SELECT TOP(1) Payload FROM dbo.CapitalSnapshots WHERE AccountKey=@account ORDER BY ObservedAt DESC,Id;";
        await using var command = new SqlCommand(query, connection);
        this.Account(command);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string text ? Read<T>(text) : null;
    }

    private async Task<SqlConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private void Account(SqlCommand command) =>
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = accountKey;

    private static void Payload<T>(SqlCommand command, string name, T value) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(value, RiskJson.Options);

    private static T Read<T>(string text) =>
        JsonSerializer.Deserialize<T>(text, RiskJson.Options)
        ?? throw new InvalidDataException("Risk evidence is unavailable.");
}
