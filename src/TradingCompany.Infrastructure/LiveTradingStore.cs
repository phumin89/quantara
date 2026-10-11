using System.Text.Json;
using Npgsql;
using TradingCompany.Application;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

public sealed class LiveTradingStore(string connectionString) : ILiveTradingStore
{
    private readonly string connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString();

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Connect(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var schemaLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('quantara-live-schema',0))", connection, transaction))
            await schemaLock.ExecuteNonQueryAsync(cancellationToken);
        using var script = new StreamReader(typeof(LiveTradingStore).Assembly.GetManifestResourceStream("TradingCompany.Infrastructure.LiveMigrations.001_ledger.sql")
            ?? throw new InvalidOperationException("Live ledger migration is missing."));
        await using var command = new NpgsqlCommand(await script.ReadToEndAsync(cancellationToken), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CreateAsync(LiveAccountState account, CancellationToken cancellationToken = default)
    {
        if (account.AccountId.Value == Guid.Empty || account.Snapshot.AccountId != account.AccountId || account.Mandate.AccountId != account.AccountId)
            throw new InvalidDataException("Account identity mismatch.");
        await using var connection = await Connect(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO live.accounts(id,revision,document) VALUES (@id,1,@document::jsonb)", connection);
        command.Parameters.AddWithValue("id", account.AccountId.Value);
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(account));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LiveSnapshot> ReadAsync(LiveAccountId id, CancellationToken cancellationToken = default)
        => (await ReadAccountAsync(id, cancellationToken)).Snapshot;
    public async Task<bool> ExistsAsync(LiveAccountId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Connect(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM live.accounts WHERE id=@id)", connection);
        command.Parameters.AddWithValue("id", id.Value);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
    public async Task<LiveAccountState> ReadAccountAsync(LiveAccountId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Connect(cancellationToken);
        return (await Load(connection, id, cancellationToken)).Account;
    }

    public async Task<IReadOnlyList<DailyMeetingAttempt>> ReadMeetingsAsync(LiveAccountId id, CancellationToken cancellationToken = default)
    {
        await using var connection = await Connect(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE((SELECT jsonb_agg(event->>'Detail')
                FROM jsonb_array_elements(document->'Audit') event WHERE event->>'Kind'=@kind), '[]'::jsonb)::text
            FROM live.accounts WHERE id=@id
                AND lower(document#>>'{AccountId,Value}')=@id::text
                AND lower(document#>>'{Snapshot,AccountId,Value}')=@id::text
                AND lower(document#>>'{Mandate,AccountId,Value}')=@id::text
            """, connection);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("kind", LiveMeetingReports.AuditKind);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidDataException("Live account is not initialized.");
        return JsonSerializer.Deserialize<string[]>(json)!.Select(detail => JsonSerializer.Deserialize<DailyMeetingAttempt>(detail)
            ?? throw new InvalidDataException("Invalid saved meeting.")).OrderByDescending(a => a.BusinessDate)
            .ThenByDescending(a => a.ReviewHour ?? 9).ThenByDescending(a => a.ReviewMinute).ToArray();
    }

    public async Task<T> WithAccountLockAsync<T>(LiveAccountId id, Func<ILiveAccountTransaction, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // ponytail: one serialized writer per account; scale to multiple accounts, not concurrent money writers.
        await using var connection = await Connect(cancellationToken);
        using (var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            // Reconciliation holds this lease across two bounded broker scans. Npgsql's
            // default 30-second query timeout is shorter than a healthy full-catalog cycle.
            // Keep a bounded, cancellable wait; never skip ownership or reconnect a lease.
            acquisition.CancelAfter(TimeSpan.FromSeconds(60));
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@id,0))", connection)
                { CommandTimeout = 0 };
            command.Parameters.AddWithValue("id", id.Value.ToString());
            await command.ExecuteNonQueryAsync(acquisition.Token);
        }
        var loaded = await Load(connection, id, cancellationToken);
        var revision = loaded.Revision;
        var committed = loaded.Account;
        var account = new LiveAccountTransaction(loaded.Account, async (state, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (ReferenceEquals(state, committed)) return;
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand("UPDATE live.accounts SET document=@document::jsonb,revision=revision+1 WHERE id=@id AND revision=@revision", connection, transaction);
            command.Parameters.AddWithValue("document", JsonSerializer.Serialize(state));
            command.Parameters.AddWithValue("id", id.Value); command.Parameters.AddWithValue("revision", revision);
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidDataException("Live account changed outside its lease.");
            await transaction.CommitAsync(ct);
            revision++;
            committed = state;
        }, async ct =>
        {
            // Never reconnect this connection: losing its session also loses account ownership.
            await using var probe = new NpgsqlCommand("SELECT 1", connection);
            await probe.ExecuteScalarAsync(ct);
        });
        // Closing this non-pooled connection releases the session lock, including on cancellation/failure.
        return await action(account, cancellationToken);
    }

    private async Task<NpgsqlConnection> Connect(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<(LiveAccountState Account, long Revision)> Load(NpgsqlConnection connection, LiveAccountId id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT document::text,revision FROM live.accounts WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("Live account is not initialized.");
        var account = JsonSerializer.Deserialize<LiveAccountState>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid live account.");
        if (account.AccountId != id || account.Snapshot.AccountId != id || account.Mandate.AccountId != id)
            throw new InvalidDataException("Stored account identity mismatch.");
        return (account, reader.GetInt64(1));
    }
}
