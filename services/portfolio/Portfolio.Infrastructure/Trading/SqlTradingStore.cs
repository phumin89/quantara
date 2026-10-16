using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Infrastructure;

public sealed class SqlTradingStore : ITradingStore
{
    private static readonly JsonSerializerOptions jsonOptions = TradingJson.Options;

    private static readonly string[] mirrorTables =
    [
        "TradingRuleVersions",
        "TradingMandates",
        "TradingDecisions",
        "TradingOrderIntents",
        "TradingFills",
        "TradingPositions",
        "TradingRiskCounters",
        "TradingReportDeliveries",
        "TradingAlerts",
    ];

    private readonly string connectionString;

    private readonly string accountKey;

    private readonly TimeProvider time;

    public SqlTradingStore(string connectionString, string accountKey, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(time);
        if (
            accountKey.Length != 64
            || accountKey.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
        )
        {
            throw new ArgumentException("Trading storage requires the canonical account hash.", nameof(accountKey));
        }

        this.connectionString = connectionString;
        this.accountKey = accountKey;
        this.time = time;
    }

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var schema = new SqlCommand(
            "IF OBJECT_ID(N'dbo.TradingRuntime',N'U') IS NULL CREATE TABLE dbo.TradingRuntime(AccountKey char(64) NOT NULL PRIMARY KEY,Revision bigint NOT NULL,Payload nvarchar(max) NOT NULL);",
            connection
        );
        await schema.ExecuteNonQueryAsync(cancellationToken);
        foreach (var table in SqlTradingStore.mirrorTables)
        {
            await using var command = new SqlCommand(
                $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL CREATE TABLE dbo.{table}(AccountKey char(64) NOT NULL,Id nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,Payload nvarchar(max) NOT NULL,PRIMARY KEY(AccountKey,Id));",
                connection
            );
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var find = new SqlCommand(
            "SELECT Revision FROM dbo.TradingRuntime WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account;",
            connection,
            transaction
        );
        this.Account(find);
        if (await find.ExecuteScalarAsync(cancellationToken) is null)
        {
            var initial = TradingStateFactory.Create(this.time.GetUtcNow());
            await using var insert = new SqlCommand(
                "INSERT INTO dbo.TradingRuntime(AccountKey,Revision,Payload) VALUES(@account,@revision,@payload);",
                connection,
                transaction
            );
            this.Account(insert);
            SqlTradingStore.Runtime(insert, initial);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await this.SaveMirrors(connection, transaction, initial, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TradingState> Read(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Revision,Payload FROM dbo.TradingRuntime WHERE AccountKey=@account;",
            connection
        );
        this.Account(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Trading storage has not been initialized for this account.");
        }

        var state =
            JsonSerializer.Deserialize<TradingState>(reader.GetString(1), SqlTradingStore.jsonOptions)
            ?? throw new InvalidDataException("Stored trading state is missing.");
        if (state.Revision != reader.GetInt64(0))
        {
            throw new InvalidDataException("Stored trading revision does not match its payload.");
        }

        this.Validate(state);
        return state;
    }

    public async Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || state.Revision != expectedRevision + 1)
        {
            throw new ArgumentException("Trading state must advance exactly one revision.", nameof(expectedRevision));
        }

        this.Validate(state);
        await using var connection = new SqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (state.Mode == TradingMode.AutoLive)
        {
            await using var accountLock = new SqlCommand(
                "DECLARE @result int; EXEC @result=sp_getapplock @Resource=@account,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; IF @result<0 THROW 50000,'Trading account is busy.',1;",
                connection,
                transaction
            );
            this.Account(accountLock);
            await accountLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var find = new SqlCommand(
            "SELECT Revision,Payload FROM dbo.TradingRuntime WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account;",
            connection,
            transaction
        );
        this.Account(find);
        TradingState? previous = null;
        await using (var reader = await find.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException("Trading storage has not been initialized for this account.");
            }

            if (reader.GetInt64(0) == expectedRevision)
            {
                previous =
                    JsonSerializer.Deserialize<TradingState>(reader.GetString(1), SqlTradingStore.jsonOptions)
                    ?? throw new InvalidDataException("Stored trading state is missing.");
            }
        }

        if (previous is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (previous.Revision != expectedRevision)
        {
            throw new InvalidDataException("Stored trading revision does not match its payload.");
        }

        this.Validate(previous);
        if (state.Mode == TradingMode.AutoLive && previous.Mode != TradingMode.AutoLive)
        {
            await using var pending = new SqlCommand(
                "IF EXISTS(SELECT 1 FROM dbo.OrderProposals WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND JSON_VALUE(Payload,'$.Sizing.PolicyHash') IS NOT NULL AND (State IN ('Submitting','Unknown','Submitted','PartiallyFilled') OR State='Pending' AND ExpiresAt>@now)) THROW 50000,'Complete adaptive manual orders before enabling Auto Trading.',1;",
                connection,
                transaction
            );
            this.Account(pending);
            pending.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = this.time.GetUtcNow();
            await pending.ExecuteNonQueryAsync(cancellationToken);
        }

        await this.SaveMirrors(connection, transaction, state, previous, cancellationToken);
        await using var update = new SqlCommand(
            "UPDATE dbo.TradingRuntime SET Revision=@revision,Payload=@payload WHERE AccountKey=@account;",
            connection,
            transaction
        );
        this.Account(update);
        SqlTradingStore.Runtime(update, state);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidDataException("Trading runtime disappeared during the transaction.");
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private void Validate(TradingState state)
    {
        state.Rules.Validate();
        if (!StringComparer.Ordinal.Equals(state.RulesHash, TradingRuleHash.Compute(state.Rules)))
        {
            throw new InvalidDataException("Trading rules do not match their version hash.");
        }

        if (state.Mandate is not null && !StringComparer.Ordinal.Equals(state.Mandate.AccountKey, this.accountKey))
        {
            throw new InvalidDataException("Trading mandate belongs to another account.");
        }
    }

    private async Task SaveMirrors(
        SqlConnection connection,
        SqlTransaction transaction,
        TradingState state,
        TradingState? previous,
        CancellationToken cancellationToken
    )
    {
        if (previous?.RulesHash != state.RulesHash)
        {
            await this.Store(
                connection,
                transaction,
                "TradingRuleVersions",
                state.RulesHash,
                state.Rules,
                true,
                cancellationToken
            );
        }

        var mandateId = state.Mandate?.Id ?? previous?.Mandate?.Id;
        if (
            mandateId is not null
            && (
                previous?.Mandate != state.Mandate
                || previous?.Mode != state.Mode
                || previous?.RulesHash != state.RulesHash
            )
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingMandates",
                $"{mandateId.Value:N}:{state.Revision.ToString(CultureInfo.InvariantCulture)}",
                new
                {
                    state.Revision,
                    state.Mode,
                    state.RulesHash,
                    state.Mandate,
                },
                true,
                cancellationToken
            );
        }

        foreach (
            var decision in SqlTradingStore.Changed(
                state.Decisions,
                previous?.Decisions ?? [],
                decision => $"{decision.Mode}:{decision.Key}",
                true
            )
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingDecisions",
                $"{decision.Mode}:{decision.Key}",
                decision,
                true,
                cancellationToken
            );
        }

        await this.SaveLedger(
            connection,
            transaction,
            state,
            TradingMode.Paper,
            state.Paper,
            previous?.Paper,
            cancellationToken
        );
        await this.SaveLedger(
            connection,
            transaction,
            state,
            TradingMode.AutoLive,
            state.Live,
            previous?.Live,
            cancellationToken
        );
        foreach (
            var report in SqlTradingStore.Changed(
                state.Reports,
                previous?.Reports ?? [],
                report => $"{report.Date}:{report.Mode}:{report.Revision.ToString(CultureInfo.InvariantCulture)}",
                false
            )
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingReportDeliveries",
                $"{report.Date}:{report.Mode}:{report.Revision.ToString(CultureInfo.InvariantCulture)}",
                report,
                false,
                cancellationToken
            );
        }

        foreach (var alert in SqlTradingStore.Changed(state.Alerts, previous?.Alerts ?? [], alert => alert.Key, false))
        {
            await this.Store(connection, transaction, "TradingAlerts", alert.Key, alert, false, cancellationToken);
        }
    }

    private async Task SaveLedger(
        SqlConnection connection,
        SqlTransaction transaction,
        TradingState state,
        TradingMode mode,
        TradingLedger ledger,
        TradingLedger? previous,
        CancellationToken cancellationToken
    )
    {
        var priorIntents = (previous?.Orders ?? []).ToDictionary(intent => intent.Id);
        foreach (var intent in ledger.Orders)
        {
            if (
                priorIntents.TryGetValue(intent.Id, out var old)
                && (
                    intent.Symbol != old.Symbol
                    || intent.Side != old.Side
                    || intent.Amount != old.Amount
                    || intent.LimitPrice != old.LimitPrice
                    || intent.CreatedAt != old.CreatedAt
                    || intent.Candle != old.Candle
                    || intent.RulesHash != old.RulesHash
                    || intent.Reason != old.Reason
                )
            )
            {
                throw new InvalidDataException("The immutable trading intent was changed.");
            }
        }

        foreach (
            var intent in SqlTradingStore.Changed(
                ledger.Orders,
                previous?.Orders ?? [],
                intent => intent.Id.ToString("N"),
                false
            )
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingOrderIntents",
                $"{mode}:{intent.Id:N}",
                new { Mode = mode, Intent = intent },
                false,
                cancellationToken
            );
        }

        foreach (
            var fill in SqlTradingStore.Changed(ledger.Fills, previous?.Fills ?? [], fill => fill.BrokerFillId, true)
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingFills",
                $"{mode}:{fill.BrokerFillId}",
                new { Mode = mode, Fill = fill },
                true,
                cancellationToken
            );
        }

        foreach (
            var position in SqlTradingStore.Changed(
                ledger.Positions,
                previous?.Positions ?? [],
                position => position.Symbol,
                false
            )
        )
        {
            await this.Store(
                connection,
                transaction,
                "TradingPositions",
                $"{mode}:{position.Symbol}",
                new { Mode = mode, Position = position },
                false,
                cancellationToken
            );
        }

        await this.Store(
            connection,
            transaction,
            "TradingRiskCounters",
            mode.ToString(),
            new
            {
                state.Revision,
                Mode = mode,
                ActiveMode = state.Mode,
                state.RulesHash,
                state.EmergencyStopped,
                state.HeartbeatAt,
                state.Fault,
                ledger.FundingThb,
                ledger.CashThb,
                ledger.OpenedAt,
                ActivePositions = ledger.Positions.Count(position => position.Quantity > 0m),
                Orders = ledger.Orders.Count,
                OpenOrders = ledger.Orders.Count(intent =>
                    intent.Status is "pending" or "submitting" or "submitted" or "partial" or "unknown"
                ),
                Fills = ledger.Fills.Count,
                LatestEquity = ledger.Equity.Count == 0 ? null : ledger.Equity[^1],
            },
            false,
            cancellationToken
        );
    }

    private static List<T> Changed<T>(
        IReadOnlyList<T> current,
        IReadOnlyList<T> previous,
        Func<T, string> identity,
        bool immutable
    )
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in previous)
        {
            if (!known.TryAdd(identity(value), JsonSerializer.Serialize(value, SqlTradingStore.jsonOptions)))
            {
                throw new InvalidDataException("Stored trading history contains duplicate identities.");
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changed = new List<T>();
        foreach (var value in current)
        {
            var id = identity(value);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                throw new InvalidDataException("Trading history contains invalid or duplicate identities.");
            }

            if (known.TryGetValue(id, out var stored))
            {
                if (StringComparer.Ordinal.Equals(stored, JsonSerializer.Serialize(value, SqlTradingStore.jsonOptions)))
                {
                    continue;
                }

                if (immutable)
                {
                    throw new InvalidDataException("An immutable trading history record changed.");
                }
            }

            changed.Add(value);
        }

        if (known.Keys.Any(id => !seen.Contains(id)))
        {
            throw new InvalidDataException("Retained trading history cannot be removed.");
        }

        return changed;
    }

    private async Task Store<T>(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        string id,
        T value,
        bool immutable,
        CancellationToken cancellationToken
    )
    {
        if (
            !SqlTradingStore.mirrorTables.Contains(table, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(id)
            || id.Length > 200
        )
        {
            throw new InvalidDataException("Invalid trading audit identity.");
        }

        var payload = JsonSerializer.Serialize(value, SqlTradingStore.jsonOptions);
        if (immutable)
        {
            await using var existing = new SqlCommand(
                $"SELECT Payload FROM dbo.{table} WITH(UPDLOCK,HOLDLOCK) WHERE AccountKey=@account AND Id=@id;",
                connection,
                transaction
            );
            this.Account(existing);
            existing.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = id;
            if (await existing.ExecuteScalarAsync(cancellationToken) is string stored)
            {
                if (!StringComparer.Ordinal.Equals(stored, payload))
                {
                    throw new InvalidDataException($"An immutable {table} record changed.");
                }

                return;
            }
        }

        await using var command = new SqlCommand(
            immutable
                ? $"INSERT INTO dbo.{table}(AccountKey,Id,Payload) VALUES(@account,@id,@payload);"
                : $"UPDATE dbo.{table} SET Payload=@payload WHERE AccountKey=@account AND Id=@id; IF @@ROWCOUNT=0 INSERT INTO dbo.{table}(AccountKey,Id,Payload) VALUES(@account,@id,@payload);",
            connection,
            transaction
        );
        this.Account(command);
        command.Parameters.Add("@id", SqlDbType.NVarChar, 200).Value = id;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = payload;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void Account(SqlCommand command) =>
        command.Parameters.Add("@account", SqlDbType.Char, 64).Value = this.accountKey;

    private static void Runtime(SqlCommand command, TradingState state)
    {
        command.Parameters.Add("@revision", SqlDbType.BigInt).Value = state.Revision;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(
            state,
            SqlTradingStore.jsonOptions
        );
    }
}
