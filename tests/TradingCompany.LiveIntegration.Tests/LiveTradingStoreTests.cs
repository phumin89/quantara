using TradingCompany.Domain;
using TradingCompany.Application;
using TradingCompany.Infrastructure;
using Xunit;
using Npgsql;
using System.Text.Json;

namespace TradingCompany.LiveIntegration.Tests;

[Collection("Live database")]
public sealed class LiveTradingStoreTests(PostgresFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pre_budget_account_can_commit_without_rewriting_immutable_intent_evidence()
    {
        var (store, state, intent) = await Setup();
        var oldAccount = state with { AccountId = new(Guid.NewGuid()) };
        oldAccount = oldAccount with { Mandate = oldAccount.Mandate with { AccountId = oldAccount.AccountId },
            Snapshot = oldAccount.Snapshot with { AccountId = oldAccount.AccountId } };
        var tx = new LiveAccountTransaction(oldAccount, (_, _) => Task.CompletedTask);
        tx.Reserve(intent with { AccountId = oldAccount.AccountId }, Now);
        var document = JsonSerializer.SerializeToNode(tx.Account)!.AsObject();
        document["Orders"]![0]!["Intent"]!.AsObject().Remove("BuyBudget");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand("INSERT INTO live.accounts(id,revision,document) VALUES (@id,1,@document::jsonb)", connection);
        insert.Parameters.AddWithValue("id", oldAccount.AccountId.Value);
        insert.Parameters.AddWithValue("document", document.ToJsonString());
        await insert.ExecuteNonQueryAsync();
        await store.WithAccountLockAsync(oldAccount.AccountId, async (locked, ct) =>
        { locked.SetStopped(true); await locked.CommitAsync(ct); return 0; });
        var restored = await store.ReadAccountAsync(oldAccount.AccountId);
        Assert.Null(Assert.Single(restored.Orders).Intent.BuyBudget);
        Assert.True(restored.Gate.Stopped);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public async Task Buy_budget_and_improved_fill_survive_restart_and_overspend_rolls_back()
    {
        var (store, state, original) = await Setup();
        var intent = original with { Quantity = .004m, BuyBudget = 400m };
        var fill = new BrokerFill(state.AccountId, "budget-fill", "budget-order", OrderSide.Buy, "BTC-THB", .00499m, 80000m, .8m, 1);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            Assert.Equal(LiveOrderState.Reserved, tx.Reserve(intent, Now).State);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "budget-order", Now);
            tx.ApplyFill(fill, Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        var restarted = new LiveTradingStore(database.ConnectionString);
        var restored = await restarted.ReadAccountAsync(state.AccountId);
        Assert.Equal(400m, Assert.Single(restored.Orders).Intent.BuyBudget);
        Assert.Equal(600m, restored.Snapshot.CashBalance);
        Assert.Equal(.00499m, Assert.Single(restored.Snapshot.Positions).Quantity);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.ApplyFill(fill with { Revision = 2, CashFee = .81m }, Now); await tx.CommitAsync(ct); return 0; }));
        var unchanged = await restarted.ReadAccountAsync(state.AccountId);
        Assert.Equal(600m, unchanged.Snapshot.CashBalance);
        Assert.Equal(1, Assert.Single(unchanged.Fills).Revision);
        await restarted.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.ApplyFill(fill, Now); tx.RecordTransition(intent.Id, LiveOrderState.Filled, null, Now); await tx.CommitAsync(ct); return 0; });
        Assert.Equal(0m, (await restarted.ReadAsync(state.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Concurrent_duplicate_intent_is_reserved_once_and_survives_restart()
    {
        var (store, state, intent) = await Setup();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { var order = tx.Reserve(intent, Now); await tx.CommitAsync(ct); return order; })));
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(state.AccountId);
        Assert.Single(restored.Orders);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
        Assert.Equal(700m, restored.Snapshot.PendingBuyNotional);
    }

    [Fact]
    public async Task Competing_intents_cannot_spend_the_same_cash()
    {
        var (store, state, intent) = await Setup();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            var order = tx.Reserve(intent with { Id = Guid.NewGuid() }, Now);
            await tx.CommitAsync(ct); return order;
        })));
        Assert.Single(results, o => o.State == LiveOrderState.Reserved);
        Assert.Single(results, o => o.State == LiveOrderState.Rejected);
        Assert.Equal(702.1m, (await store.ReadAsync(state.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Uncommitted_change_is_rolled_back()
    {
        var (store, state, intent) = await Setup();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WithAccountLockAsync<int>(state.AccountId, (tx, ct) =>
        { tx.Reserve(intent, Now); throw new InvalidOperationException("simulated crash before commit"); }));
        Assert.Empty((await store.ReadAccountAsync(state.AccountId)).Orders);
    }

    [Fact]
    public async Task Stop_is_durable_and_prevents_new_dispatch()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.Reserve(intent, Now); await tx.CommitAsync(ct); tx.SetStopped(true); await tx.CommitAsync(ct); return 0; });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LiveTradingStore(database.ConnectionString).WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now); await tx.CommitAsync(ct); return 0; }));
        Assert.True((await store.ReadAccountAsync(state.AccountId)).Gate.Stopped);
    }

    [Fact]
    public async Task Unknown_cannot_be_returned_to_reserved_for_a_retry()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now); tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now); await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Unknown, null, Now); await tx.CommitAsync(ct); return 0;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.RecordTransition(intent.Id, LiveOrderState.Reserved, null, Now); await tx.CommitAsync(ct); return 0; }));
        Assert.Equal(702.1m, (await store.ReadAsync(state.AccountId)).CashReserved);
    }

    [Fact]
    public async Task Duplicate_fill_is_applied_once_and_terminal_release_is_explicit()
    {
        var (store, state, intent) = await Setup();
        var fill = new BrokerFill(state.AccountId, "tx-opaque", "broker-opaque", OrderSide.Buy, "BTC-THB", .003m, 100000m, .75m, 1);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now); tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now); await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "broker-opaque", Now);
            tx.ApplyFill(fill, Now); tx.ApplyFill(fill, Now); await tx.CommitAsync(ct); return 0;
        });
        var snapshot = await store.ReadAsync(state.AccountId);
        Assert.Equal(699.25m, snapshot.CashBalance);
        Assert.Equal(.003m, Assert.Single(snapshot.Positions).Quantity);
        Assert.Equal(401.35m, snapshot.CashReserved);
        Assert.Equal(300.75m, snapshot.DailyTurnover);
        Assert.False(snapshot.Reconciled);
    }

    [Fact]
    public async Task Stale_or_conflicting_fill_revision_does_not_change_balances()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now); tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now); await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "order", Now);
            tx.ApplyFill(new(state.AccountId, "txn", "order", OrderSide.Buy, "BTC-THB", .001m, 100000, .25m, 1), Now);
            await tx.CommitAsync(ct); return 0;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.ApplyFill(new(state.AccountId, "txn", "order", OrderSide.Buy, "BTC-THB", .002m, 100000, .25m, 1), Now); await tx.CommitAsync(ct); return 0; }));
        Assert.Equal(899.75m, (await store.ReadAsync(state.AccountId)).CashBalance);
    }

    [Fact]
    public async Task Fee_correction_preserves_both_evidence_revisions_in_database()
    {
        var (store, state, intent) = await Setup();
        var fill = new BrokerFill(state.AccountId, "fee-txn", "order", OrderSide.Buy, "BTC-THB", .003m, 100000, .75m, 1);
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "order", Now);
            tx.ApplyFill(fill, Now);
            await tx.CommitAsync(ct);
            tx.ApplyFill(fill with { CashFee = .9m, Revision = 2 }, Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(699.1m, restored.Snapshot.CashBalance);
        Assert.Equal(.003m, Assert.Single(restored.Snapshot.Positions).Quantity);
        Assert.Equal(300.9m, restored.Snapshot.DailyTurnover);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT evidence::text FROM live.fill_evidence WHERE account_id=@id ORDER BY revision", connection);
        command.Parameters.AddWithValue("id", state.AccountId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var fees = new List<decimal>();
        while (await reader.ReadAsync()) fees.Add(JsonSerializer.Deserialize<BrokerFill>(reader.GetString(0))!.CashFee);
        Assert.Equal(new[] { .75m, .9m }, fees);
    }

    [Theory]
    [InlineData("UPDATE live.account_history SET document='{}'::jsonb WHERE account_id=@id")]
    [InlineData("DELETE FROM live.account_history WHERE account_id=@id")]
    public async Task Persisted_history_cannot_be_rewritten(string sql)
    {
        var (_, state, _) = await Setup();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", state.AccountId.Value);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("55000", error.SqlState);
    }

    [Fact]
    public async Task Lost_database_session_cannot_commit_dispatch_authorization()
    {
        var (_, state, intent) = await Setup();
        var application = "lost-lease-" + Guid.NewGuid().ToString("N");
        var store = new LiveTradingStore(new NpgsqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = application }.ToString());
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now);
            await tx.CommitAsync(ct);
            await using var admin = new NpgsqlConnection(database.ConnectionString);
            await admin.OpenAsync(ct);
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name=@name", admin);
            kill.Parameters.AddWithValue("name", application);
            Assert.True((bool)(await kill.ExecuteScalarAsync(ct))!);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            return 0;
        }));
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(LiveOrderState.Reserved, Assert.Single(restored.Orders).State);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public async Task Stop_holding_account_lock_wins_against_waiting_dispatch()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.Reserve(intent, Now); await tx.CommitAsync(ct); return 0; });
        var stopOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            stopOwnsLock.SetResult();
            await releaseStop.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            tx.SetStopped(true);
            await tx.CommitAsync(ct);
            return 0;
        });
        await stopOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var dispatch = store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now); await tx.CommitAsync(ct); return 0; });
        releaseStop.SetResult();
        await stop;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch);
        Assert.Equal(LiveOrderState.Reserved, Assert.Single((await store.ReadAccountAsync(state.AccountId)).Orders).State);
    }

    [Fact]
    public async Task Same_intent_id_with_changed_content_cannot_replace_reservation()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.Reserve(intent, Now); await tx.CommitAsync(ct); return 0; });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.Reserve(intent with { Quantity = .001m }, Now); await tx.CommitAsync(ct); return 0; }));
        Assert.Equal(intent, Assert.Single((await store.ReadAccountAsync(state.AccountId)).Orders).Intent);
    }

    [Fact]
    public async Task Revisions_applied_in_one_commit_keep_all_fill_evidence()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "order", Now);
            var fill = new BrokerFill(state.AccountId, "revised", "order", OrderSide.Buy, "BTC-THB", .001m, 100000, .25m, 1);
            tx.ApplyFill(fill, Now);
            tx.ApplyFill(fill with { Revision = 2, CashFee = .3m }, Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM live.fill_evidence WHERE account_id=@id", connection);
        command.Parameters.AddWithValue("id", state.AccountId.Value);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Missing_schema_fails_closed_and_migration_preserves_paper_data()
    {
        // This database exists only in this fixture's disposable container.
        var name = "schema_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(database.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = name }.ToString();
        var store = new LiveTradingStore(connectionString);
        var called = false;
        var error = await Assert.ThrowsAsync<PostgresException>(() => store.WithAccountLockAsync(new(Guid.NewGuid()), (tx, ct) =>
        { called = true; return Task.FromResult(0); }));
        Assert.Equal("42P01", error.SqlState);
        Assert.False(called);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("CREATE TABLE public.paper_sentinel(amount numeric); INSERT INTO public.paper_sentinel VALUES (123.4567890123456789)", connection))
            await seed.ExecuteNonQueryAsync();
        await store.EnsureSchemaAsync();
        await store.EnsureSchemaAsync();
        await using var read = new NpgsqlCommand("SELECT amount FROM public.paper_sentinel", connection);
        Assert.Equal(123.4567890123456789m, await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Database_outage_never_invokes_money_callback()
    {
        var isolated = new PostgresFixture();
        await isolated.InitializeAsync();
        try
        {
            var store = new LiveTradingStore(isolated.ConnectionString);
            await store.EnsureSchemaAsync();
            await isolated.StopAsync();
            var called = false;
            await Assert.ThrowsAnyAsync<NpgsqlException>(() => store.WithAccountLockAsync(new(Guid.NewGuid()), (tx, ct) =>
            { called = true; return Task.FromResult(0); }));
            Assert.False(called);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task Two_owned_orders_cannot_claim_same_broker_identity()
    {
        var (store, state, first) = await Setup();
        first = first with { Quantity = .001m };
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(first, Now);
            tx.RecordTransition(first.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.RecordTransition(first.Id, LiveOrderState.Acknowledged, "same-broker-id", Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            var second = first with { Id = Guid.NewGuid(), SnapshotVersion = tx.Account.Snapshot.Version };
            Assert.Equal(LiveOrderState.Reserved, tx.Reserve(second, Now).State);
            tx.RecordTransition(second.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.RecordTransition(second.Id, LiveOrderState.Acknowledged, "same-broker-id", Now);
            await tx.CommitAsync(ct);
            return 0;
        }));
    }

    [Fact]
    public async Task Stop_does_not_hide_an_already_dispatched_order_or_release_its_cash()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.SetStopped(true);
            await tx.CommitAsync(ct);
            return 0;
        });
        var restored = await new LiveTradingStore(database.ConnectionString).ReadAccountAsync(state.AccountId);
        Assert.True(restored.Gate.Stopped);
        Assert.False(restored.Gate.Armed);
        Assert.Equal(LiveOrderState.Dispatching, Assert.Single(restored.Orders).State);
        Assert.Equal(702.1m, restored.Snapshot.CashReserved);
    }

    [Fact]
    public async Task Database_evidence_conflict_rolls_back_balance_and_order_update_atomically()
    {
        var (store, state, intent) = await Setup();
        await store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        {
            tx.Reserve(intent, Now);
            tx.RecordTransition(intent.Id, LiveOrderState.Dispatching, null, Now);
            await tx.CommitAsync(ct);
            tx.RecordTransition(intent.Id, LiveOrderState.Acknowledged, "order", Now);
            await tx.CommitAsync(ct);
            return 0;
        });
        var fill = new BrokerFill(state.AccountId, "conflict", "order", OrderSide.Buy, "BTC-THB", .001m, 100000, .25m, 1);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("INSERT INTO live.fill_evidence(account_id,transaction_id,revision,evidence) VALUES (@id,'conflict',1,@evidence::jsonb)", connection))
        {
            seed.Parameters.AddWithValue("id", state.AccountId.Value);
            seed.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(fill with { CashFee = .5m }));
            await seed.ExecuteNonQueryAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => store.WithAccountLockAsync(state.AccountId, async (tx, ct) =>
        { tx.ApplyFill(fill, Now); await tx.CommitAsync(ct); return 0; }));
        Assert.Equal("23514", error.SqlState);
        var restored = await store.ReadAccountAsync(state.AccountId);
        Assert.Equal(1000, restored.Snapshot.CashBalance);
        Assert.Equal(0, Assert.Single(restored.Orders).FilledQuantity);
        Assert.Empty(restored.Fills);
        Assert.DoesNotContain(restored.Audit, a => a.Kind == "FillApplied");
    }

    internal async Task<(LiveTradingStore Store, LiveAccountState State, LiveIntent Intent)> Setup()
    {
        var store = new LiveTradingStore(database.ConnectionString);
        await store.EnsureSchemaAsync();
        var id = new LiveAccountId(Guid.NewGuid());
        var mandate = new LiveMandate(id, 1, "test-key", 1000, 800, 5000, 100, .2m, .8m, 3, .01m, .003m, ["BTC-THB"], null);
        var snapshot = new LiveSnapshot(id, 1, Now, 1000, 0, 0, [], 1000, 0, 0, 0, true, [new("BTC-THB", 99900, 100000, Now)]);
        var state = new LiveAccountState(id, mandate, new(true, false, "test-key", true, 1), snapshot, [], [], []);
        await store.CreateAsync(state);
        return (store, state, new(id, Guid.NewGuid(), Guid.NewGuid(), TradingMode.Live, "BTC-THB", OrderSide.Buy, .007m, 100000, 1, 1, "test-evidence"));
    }
}
