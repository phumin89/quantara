using System.Text.Json;
using Npgsql;
using TradingCompany.Domain;

namespace TradingCompany.Infrastructure;

public sealed class BitkubOrderReceiptJournal
{
    private readonly string connectionString;
    private readonly LiveAccountId accountId;
    private readonly string credentialFingerprint;

    public BitkubOrderReceiptJournal(string connectionString, LiveAccountId accountId, string credentialFingerprint)
    {
        if (accountId.Value == Guid.Empty || string.IsNullOrWhiteSpace(credentialFingerprint))
            throw new ArgumentException("Bitkub receipt journal identity is invalid.");
        this.connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ToString();
        this.accountId = accountId;
        this.credentialFingerprint = credentialFingerprint;
    }

    public async Task AppendAsync(BitkubOrderReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateIdentity(receipt.OrderId, receipt.ClientId);
        await CheckHealthyAsync(cancellationToken);
        var document = JsonSerializer.Serialize(receipt);
        await using var connection = await Connect(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO live.bitkub_order_receipts(account_id,credential_fingerprint,order_id,updated_at,client_id,document)
            VALUES (@account,@fingerprint,@order,@updated,@client,@document::jsonb)
            ON CONFLICT (account_id,credential_fingerprint,order_id,updated_at) DO NOTHING
            """, connection, transaction))
        {
            AddIdentity(insert, receipt.OrderId);
            insert.Parameters.AddWithValue("updated", receipt.UpdatedAt);
            insert.Parameters.AddWithValue("client", receipt.ClientId);
            insert.Parameters.AddWithValue("document", document);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var conflict = false;
        await using (var compare = new NpgsqlCommand("""
            SELECT document=@document::jsonb AND client_id=@client
            FROM live.bitkub_order_receipts
            WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order AND updated_at=@updated
            """, connection, transaction))
        {
            AddIdentity(compare, receipt.OrderId);
            compare.Parameters.AddWithValue("updated", receipt.UpdatedAt);
            compare.Parameters.AddWithValue("client", receipt.ClientId);
            compare.Parameters.AddWithValue("document", document);
            conflict = await compare.ExecuteScalarAsync(cancellationToken) is not true;
        }
        if (conflict)
        {
            await using var marker = new NpgsqlCommand("""
                INSERT INTO live.bitkub_receipt_conflicts(account_id,credential_fingerprint,order_id,updated_at,document)
                VALUES (@account,@fingerprint,@order,@updated,@document::jsonb) ON CONFLICT DO NOTHING
                """, connection, transaction);
            AddIdentity(marker, receipt.OrderId);
            marker.Parameters.AddWithValue("updated", receipt.UpdatedAt);
            marker.Parameters.AddWithValue("document", document);
            await marker.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        if (conflict) throw new InvalidDataException("Conflicting Bitkub order receipt.");
    }

    public async Task CheckHealthyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await Connect(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM live.bitkub_receipt_conflicts
            WHERE account_id=@account AND credential_fingerprint=@fingerprint)
            """, connection);
        command.Parameters.AddWithValue("account", accountId.Value);
        command.Parameters.AddWithValue("fingerprint", credentialFingerprint);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
            throw new InvalidDataException("Conflicting Bitkub order receipt.");
    }

    public async Task<BitkubOrderReceipt?> ReadLatestAsync(string orderId, string clientId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(orderId, clientId);
        await CheckHealthyAsync(cancellationToken);
        await using var connection = await Connect(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT document::text,client_id,updated_at FROM live.bitkub_order_receipts
            WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order
            ORDER BY updated_at DESC LIMIT 1
            """, connection);
        AddIdentity(command, orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!string.Equals(reader.GetString(1), clientId, StringComparison.Ordinal))
            throw new InvalidDataException("Stored Bitkub order receipt identity mismatch.");
        BitkubOrderReceipt? receipt;
        try { receipt = JsonSerializer.Deserialize<BitkubOrderReceipt>(reader.GetString(0)); }
        catch (JsonException) { throw new InvalidDataException("Stored Bitkub order receipt is invalid."); }
        if (receipt is null || !string.Equals(receipt.OrderId, orderId, StringComparison.Ordinal)
            || !string.Equals(receipt.ClientId, clientId, StringComparison.Ordinal) || receipt.UpdatedAt != reader.GetInt64(2))
            throw new InvalidDataException("Stored Bitkub order receipt identity mismatch.");
        return receipt;
    }

    private void AddIdentity(NpgsqlCommand command, string orderId)
    {
        command.Parameters.AddWithValue("account", accountId.Value);
        command.Parameters.AddWithValue("fingerprint", credentialFingerprint);
        command.Parameters.AddWithValue("order", orderId);
    }

    private static void ValidateIdentity(string orderId, string clientId)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidDataException("Bitkub order receipt identity is invalid.");
    }

    private async Task<NpgsqlConnection> Connect(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
