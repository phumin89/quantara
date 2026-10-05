using Npgsql;
using System.Text.Json;
using TradingCompany.Application;

namespace TradingCompany.Infrastructure;

// Sandbox report database is deliberately independent of Live account storage.
public sealed class PostgresCompanyConversationStore(string connectionString)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task ImportAsync(IReadOnlyList<CompanyConversation> days, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var schema = new NpgsqlCommand("""
            CREATE SCHEMA IF NOT EXISTS sandbox_reports;
            CREATE TABLE IF NOT EXISTS sandbox_reports.days (business_date date PRIMARY KEY, status text, updated_at timestamptz NOT NULL);
            ALTER TABLE sandbox_reports.days ADD COLUMN IF NOT EXISTS execution_verified boolean NOT NULL DEFAULT false;
            CREATE TABLE IF NOT EXISTS sandbox_reports.messages (id uuid NOT NULL, business_date date NOT NULL REFERENCES sandbox_reports.days, document jsonb NOT NULL, PRIMARY KEY(business_date,id));
            CREATE INDEX IF NOT EXISTS messages_date ON sandbox_reports.messages(business_date);
            """, connection, transaction)) await schema.ExecuteNonQueryAsync(cancellationToken);
        foreach (var day in days)
        {
            await using var upsertDay = new NpgsqlCommand("""
                INSERT INTO sandbox_reports.days (business_date,status,updated_at,execution_verified) VALUES (@day,@status,@updated,@verified)
                ON CONFLICT (business_date) DO UPDATE SET status=EXCLUDED.status, updated_at=EXCLUDED.updated_at, execution_verified=EXCLUDED.execution_verified
                """, connection, transaction);
            upsertDay.Parameters.AddWithValue("day", day.BusinessDate);
            upsertDay.Parameters.AddWithValue("status", NpgsqlTypes.NpgsqlDbType.Text, (object?)day.Status ?? DBNull.Value);
            upsertDay.Parameters.AddWithValue("updated", day.UpdatedAt);
            upsertDay.Parameters.AddWithValue("verified", day.ExecutionVerified);
            await upsertDay.ExecuteNonQueryAsync(cancellationToken);
            foreach (var message in day.Messages)
            {
                await using var upsertMessage = new NpgsqlCommand("""
                    INSERT INTO sandbox_reports.messages VALUES (@id,@day,@document::jsonb)
                    ON CONFLICT (business_date,id) DO UPDATE SET document=EXCLUDED.document
                    WHERE sandbox_reports.messages.business_date=EXCLUDED.business_date
                      AND (sandbox_reports.messages.document - 'evidenceStatus')=(EXCLUDED.document - 'evidenceStatus')
                    """, connection, transaction);
                upsertMessage.Parameters.AddWithValue("id", message.Id);
                upsertMessage.Parameters.AddWithValue("day", day.BusinessDate);
                upsertMessage.Parameters.AddWithValue("document", JsonSerializer.Serialize(message, Json));
                if (await upsertMessage.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("Conflicting report identity; database import rolled back.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ConversationDates> DatesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT business_date,updated_at FROM sandbox_reports.days ORDER BY business_date DESC LIMIT 366", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var dates = new List<DateOnly>();
        var updated = DateTimeOffset.MinValue;
        while (await reader.ReadAsync(cancellationToken))
        {
            dates.Add(reader.GetFieldValue<DateOnly>(0));
            var at = reader.GetFieldValue<DateTime>(1);
            if (at > updated) updated = at;
        }
        return new(dates, updated == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : updated);
    }

    public async Task<CompanyConversation> ReadAsync(DateOnly day, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        string? status = null;
        var verified = false;
        var updated = DateTimeOffset.UtcNow;
        await using (var header = new NpgsqlCommand("SELECT status,updated_at,execution_verified FROM sandbox_reports.days WHERE business_date=@day", connection, transaction))
        {
            header.Parameters.AddWithValue("day", day);
            await using var reader = await header.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) { status = reader.IsDBNull(0) ? null : reader.GetString(0); updated = reader.GetFieldValue<DateTime>(1); verified = reader.GetBoolean(2); }
        }
        await using var command = new NpgsqlCommand("SELECT document::text FROM sandbox_reports.messages WHERE business_date=@day ORDER BY document->>'createdAt', id LIMIT 512", connection, transaction);
        command.Parameters.AddWithValue("day", day);
        var messages = new List<ConversationMessage>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) messages.Add(JsonSerializer.Deserialize<ConversationMessage>(reader.GetString(0), Json)!);
        await transaction.CommitAsync(cancellationToken);
        return new(day, status, messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToArray(), updated, verified);
    }
}
