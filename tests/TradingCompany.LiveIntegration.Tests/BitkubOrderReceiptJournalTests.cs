using System.Text.Json;
using Npgsql;
using TradingCompany.Domain;
using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class BitkubOrderReceiptJournalTests(PostgresFixture database)
{
    private readonly LiveAccountId accountId = new(Guid.NewGuid());
    private const string Fingerprint = "receipt-test-key";

    [Fact]
    public async Task Duplicate_identical_receipt_is_stable_and_survives_a_fresh_journal()
    {
        var journal = await Setup();
        var receipt = Receipt();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => journal.AppendAsync(receipt)));

        Assert.Equal(receipt, await Journal().ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.Equal(1L, await CountRows(accountId, Fingerprint, receipt.OrderId));
        Assert.False(await new LiveTradingStore(database.ConnectionString).ExistsAsync(accountId));
    }

    [Fact]
    public async Task Conflicting_same_identity_is_rejected_without_replacing_evidence()
    {
        var receipt = Receipt();
        var journal = await Setup();
        await journal.AppendAsync(receipt);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => journal.AppendAsync(receipt with { CashFee = .4m }));

        Assert.Equal("Conflicting Bitkub order receipt.", error.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => Journal().ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        await Assert.ThrowsAsync<InvalidDataException>(() => Journal().AppendAsync(receipt with { UpdatedAt = receipt.UpdatedAt + 1 }));
        await new BitkubOrderReceiptJournal(database.ConnectionString, new(Guid.NewGuid()), Fingerprint).CheckHealthyAsync();
        await new BitkubOrderReceiptJournal(database.ConnectionString, accountId, "other-key").CheckHealthyAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var saved = new NpgsqlCommand("SELECT document::text FROM live.bitkub_order_receipts WHERE account_id=@account", connection);
        saved.Parameters.AddWithValue("account", accountId.Value);
        Assert.Equal(receipt, JsonSerializer.Deserialize<BitkubOrderReceipt>((string)(await saved.ExecuteScalarAsync())!));
        await using var clear = new NpgsqlCommand("DELETE FROM live.bitkub_receipt_conflicts WHERE account_id=@account", connection);
        clear.Parameters.AddWithValue("account", accountId.Value);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => clear.ExecuteNonQueryAsync())).SqlState);
        Assert.Equal(1L, await CountRows(accountId, Fingerprint, receipt.OrderId));
    }

    [Fact]
    public async Task Older_arrival_is_preserved_while_latest_revision_is_returned()
    {
        var older = Receipt();
        var newer = older with { UpdatedAt = older.UpdatedAt + 1, ExecutedAmount = 200m, ReceivedAmount = .002m, TotalFee = .5m, CashFee = .5m };
        var journal = await Setup();
        await journal.AppendAsync(newer);
        await journal.AppendAsync(older);

        Assert.Equal(newer, await journal.ReadLatestAsync(older.OrderId, older.ClientId));
        Assert.Equal(2L, await CountRows(accountId, Fingerprint, older.OrderId));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT document::text FROM live.bitkub_order_receipts
            WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order ORDER BY updated_at
            """, connection);
        AddIdentity(command, accountId, Fingerprint, older.OrderId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(older, JsonSerializer.Deserialize<BitkubOrderReceipt>(reader.GetString(0)));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(newer, JsonSerializer.Deserialize<BitkubOrderReceipt>(reader.GetString(0)));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Same_order_is_isolated_by_account_and_credential_fingerprint()
    {
        var journal = await Setup();
        var receipt = Receipt();
        var otherAccount = new LiveAccountId(Guid.NewGuid());
        var otherAccountReceipt = receipt with { ClientId = "other-account-client", OrderAmount = 300m };
        var otherKeyReceipt = receipt with { ClientId = "other-key-client", OrderAmount = 400m };
        var otherAccountJournal = new BitkubOrderReceiptJournal(database.ConnectionString, otherAccount, Fingerprint);
        var otherKeyJournal = new BitkubOrderReceiptJournal(database.ConnectionString, accountId, "other-test-key");
        await journal.AppendAsync(receipt);
        Assert.Null(await otherAccountJournal.ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.Null(await otherKeyJournal.ReadLatestAsync(receipt.OrderId, receipt.ClientId));

        await otherAccountJournal.AppendAsync(otherAccountReceipt);
        await otherKeyJournal.AppendAsync(otherKeyReceipt);

        Assert.Equal(receipt, await Journal().ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.Equal(otherAccountReceipt, await otherAccountJournal.ReadLatestAsync(receipt.OrderId, otherAccountReceipt.ClientId));
        Assert.Equal(otherKeyReceipt, await otherKeyJournal.ReadLatestAsync(receipt.OrderId, otherKeyReceipt.ClientId));
    }

    [Fact]
    public async Task Latest_receipt_with_a_different_client_is_rejected_instead_of_hidden()
    {
        var older = Receipt();
        var newer = older with { ClientId = "other-client", UpdatedAt = older.UpdatedAt + 1 };
        var journal = await Setup();
        await journal.AppendAsync(older);
        await journal.AppendAsync(newer);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => journal.ReadLatestAsync(older.OrderId, older.ClientId));

        Assert.Equal("Stored Bitkub order receipt identity mismatch.", error.Message);
        Assert.Equal(newer, await journal.ReadLatestAsync(newer.OrderId, newer.ClientId));
        Assert.Equal(2L, await CountRows(accountId, Fingerprint, older.OrderId));
    }

    [Fact]
    public async Task Schema_can_be_rerun_without_changing_receipts()
    {
        var journal = await Setup();
        var receipt = Receipt();
        await journal.AppendAsync(receipt);

        var store = new LiveTradingStore(database.ConnectionString);
        await store.EnsureSchemaAsync();
        await store.EnsureSchemaAsync();

        Assert.Equal(receipt, await Journal().ReadLatestAsync(receipt.OrderId, receipt.ClientId));
        Assert.Equal(1L, await CountRows(accountId, Fingerprint, receipt.OrderId));
    }

    [Theory]
    [InlineData("UPDATE live.bitkub_order_receipts SET document='{}'::jsonb WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order")]
    [InlineData("DELETE FROM live.bitkub_order_receipts WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order")]
    public async Task Stored_receipts_cannot_be_rewritten(string sql)
    {
        var journal = await Setup();
        var receipt = Receipt();
        await journal.AppendAsync(receipt);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddIdentity(command, accountId, Fingerprint, receipt.OrderId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("55000", error.SqlState);
        Assert.Equal(receipt, await Journal().ReadLatestAsync(receipt.OrderId, receipt.ClientId));
    }

    private BitkubOrderReceiptJournal Journal() => new(database.ConnectionString, accountId, Fingerprint);

    private async Task<BitkubOrderReceiptJournal> Setup()
    {
        await new LiveTradingStore(database.ConnectionString).EnsureSchemaAsync();
        return Journal();
    }

    private static BitkubOrderReceipt Receipt() => new("opaque-order", "owned-client", "BTC-THB", OrderSide.Buy,
        "open", 100000m, 300m, 100m, .001m, .25m, 0m, .25m, 1791248400);

    private async Task<long> CountRows(LiveAccountId id, string fingerprint, string orderId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM live.bitkub_order_receipts
            WHERE account_id=@account AND credential_fingerprint=@fingerprint AND order_id=@order
            """, connection);
        AddIdentity(command, id, fingerprint, orderId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void AddIdentity(NpgsqlCommand command, LiveAccountId id, string fingerprint, string orderId)
    {
        command.Parameters.AddWithValue("account", id.Value);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("order", orderId);
    }
}
