using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingStorageChecks
{
    public static async Task Run()
    {
        var account = new string('a', 64);
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var initial = TradingStateFactory.Create(now);
        TradingStorageChecks.Check(
            initial.Mode == TradingMode.Paper && initial.Mandate is null,
            "New storage starts in paper mode without a mandate."
        );
        var intentId = Guid.Parse("a54c6407-e1c1-4f84-9215-6a7184741abb");
        var state = initial with
        {
            Revision = 1,
            Mandate = new TradingMandate(
                Guid.Parse("52013166-6778-470b-aacb-b5edc8e72f02"),
                account,
                initial.RulesHash,
                now,
                now.AddHours(1),
                null
            ),
            Paper = initial.Paper with
            {
                Positions = [new TradingHolding("BTC", 0.1m, 100m, now, 1000m)],
                Orders =
                [
                    new TradingIntent(
                        intentId,
                        "BTC",
                        TradeSide.Buy,
                        100m,
                        1000m,
                        0m,
                        now,
                        1,
                        initial.RulesHash,
                        "fixture",
                        "filled"
                    ),
                ],
                Fills =
                [
                    new TradingFill(
                        Guid.Parse("6ce133aa-1411-42a6-ac2d-5c083796e820"),
                        intentId,
                        "fill-1",
                        "BTC",
                        TradeSide.Buy,
                        0.1m,
                        1000m,
                        100m,
                        0.25m,
                        now,
                        GrossFeeThb: 0.30m,
                        FeeCreditThb: 0.05m
                    ),
                ],
            },
            Decisions =
            [
                new TradingDecisionRecord("BTC:1", "BTC", 1, TradingAction.Buy, "fixture", now, TradingMode.Paper),
            ],
            Reports =
            [
                new TradingDailyReport(
                    "2026-10-16",
                    TradingMode.Paper,
                    1,
                    now,
                    "fixture",
                    "prepared",
                    1000m,
                    null,
                    null,
                    "fingerprint"
                ),
            ],
            Alerts = [new TradingAlert("alert-1", "fixture", "Fixture alert", now)],
        };
        var options = TradingStorageChecks.Options();
        var payload = JsonSerializer.Serialize(state, options);
        var restored =
            JsonSerializer.Deserialize<TradingState>(payload, options)
            ?? throw new InvalidOperationException("Trading state did not deserialize.");
        TradingStorageChecks.Check(
            JsonSerializer.Serialize(restored, options) == payload
                && restored.Paper.Fills.Single().FeeThb == 0.25m
                && restored.Paper.Fills.Single().GrossFeeThb == 0.30m
                && restored.Paper.Fills.Single().FeeCreditThb == 0.05m
                && restored.Paper.Fills.Single().RealizedNetThb is null
                && restored.Reports.Single().EndingEquityThb is null
                && restored.Decisions.Single().Action == TradingAction.Buy,
            "Trading storage must preserve exact decimal fees, unknown profits and nested state."
        );
        var root =
            JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidOperationException("Trading JSON is missing.");
        TradingStorageChecks.Check(
            root["mode"]?.GetValue<string>() == "paper",
            "Trading enum storage uses camel-case strings."
        );
        TradingStorageChecks.RefusedPayload(root, options, json => json.Remove("revision"));
        TradingStorageChecks.RefusedPayload(
            root,
            options,
            json => TradingStorageChecks.Object(json["paper"]).Remove("cashThb")
        );
        TradingStorageChecks.RefusedPayload(root, options, json => json["rules"] = null);
        TradingStorageChecks.RefusedPayload(root, options, json => json["mode"] = 1);
        TradingStorageChecks.RefusedPayload(root, options, json => json["mode"] = "unsupported");
        TradingStorageChecks.RefusedPayload(
            root,
            options,
            json => TradingStorageChecks.Object(json["decisions"]?.AsArray()[0])["action"] = 1
        );
        TradingStorageChecks.RefusedPayload(
            root,
            options,
            json => TradingStorageChecks.Object(json["decisions"]?.AsArray()[0])["action"] = "approve"
        );
        TradingStorageChecks.RefusedPayload(
            root,
            options,
            json =>
                TradingStorageChecks.Object(TradingStorageChecks.Object(json["paper"])["fills"]?.AsArray()[0])["side"] =
                    0
        );
        TradingStorageChecks.RefusedPayload(
            root,
            options,
            json =>
                TradingStorageChecks.Object(TradingStorageChecks.Object(json["paper"])["fills"]?.AsArray()[0])["side"] =
                    "approve"
        );

        var store = new SqlTradingStore(
            "Server=unused;Database=unused;Integrated Security=true",
            account,
            new FixedTimeProvider(now)
        );
        await TradingStorageChecks.RefusedSave(store, state, 1);
        await TradingStorageChecks.RefusedSave(store, state, -1);
        await TradingStorageChecks.RefusedSave(store, state, long.MaxValue);
        await TradingStorageChecks.RefusedSave(store, state with { RulesHash = new string('b', 64) }, 0);
        await TradingStorageChecks.RefusedSave(
            store,
            state with
            {
                Mandate = new TradingMandate(
                    Guid.NewGuid(),
                    new string('b', 64),
                    state.RulesHash,
                    now,
                    now.AddHours(1),
                    null
                ),
            },
            0
        );
        TradingStorageChecks.CheckDiffs(state);
        Console.WriteLine(
            "Trading storage checks passed: strict JSON, revision/account guards, bounded mirror diffs and immutable retained fills; no SQL connection used."
        );
    }

    private static void CheckDiffs(TradingState state)
    {
        var fill = state.Paper.Fills[0];
        var retained = Enumerable
            .Range(0, 100)
            .Select(index => fill with { BrokerFillId = $"retained-{index}" })
            .ToArray();
        TradingStorageChecks.Check(
            TradingStorageChecks.Changed(retained, retained, item => item.BrokerFillId, true).Count == 0,
            "Unchanged retained history must produce no mirror writes."
        );
        var appended = retained.Append(fill).ToArray();
        TradingStorageChecks.Check(
            TradingStorageChecks.Changed(appended, retained, item => item.BrokerFillId, true) is [var added]
                && added.BrokerFillId == fill.BrokerFillId,
            "Appending one fill must produce one mirror write regardless of retained history size."
        );
        var changed = retained.ToArray();
        changed[0] = changed[0] with { FeeThb = 1m };
        TradingStorageChecks.RefusedHistory(() =>
            TradingStorageChecks.Changed(changed, retained, item => item.BrokerFillId, true)
        );
        TradingStorageChecks.RefusedHistory(() =>
            TradingStorageChecks.Changed(
                retained.Append(retained[0] with { IntentId = Guid.NewGuid() }).ToArray(),
                retained,
                item => item.BrokerFillId,
                true
            )
        );
        TradingStorageChecks.RefusedHistory(() =>
            TradingStorageChecks.Changed(retained.Skip(1).ToArray(), retained, item => item.BrokerFillId, true)
        );
        var order = state.Paper.Orders[0];
        TradingStorageChecks.Check(
            TradingStorageChecks
                .Changed([order with { Status = "cancelled" }], new[] { order }, item => item.Id.ToString("N"), false)
                .Count == 1,
            "A changed current intent must still produce a mirror update."
        );
    }

    private static IReadOnlyList<T> Changed<T>(
        IReadOnlyList<T> current,
        IReadOnlyList<T> previous,
        Func<T, string> identity,
        bool immutable
    )
    {
        var method =
            typeof(SqlTradingStore).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Trading mirror diff is missing.");
        return method.MakeGenericMethod(typeof(T)).Invoke(null, [current, previous, identity, immutable])
                as IReadOnlyList<T>
            ?? throw new InvalidOperationException("Trading mirror diff is invalid.");
    }

    private static void RefusedHistory(Action action)
    {
        try
        {
            action();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("Changed, duplicated or missing retained fills must be refused.");
    }

    private static JsonSerializerOptions Options()
    {
        var helper =
            typeof(SqlTradingStore).Assembly.GetType("Quantara.Portfolio.Infrastructure.TradingJson")
            ?? throw new InvalidOperationException("Trading JSON helper is missing.");
        var method =
            helper.GetMethod("CreateOptions", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Trading JSON options are missing.");
        return method.Invoke(null, null) as JsonSerializerOptions
            ?? throw new InvalidOperationException("Trading JSON options are invalid.");
    }

    private static JsonObject Object(JsonNode? node) =>
        node?.AsObject() ?? throw new InvalidOperationException("Trading JSON fixture is incomplete.");

    private static void RefusedPayload(JsonObject original, JsonSerializerOptions options, Action<JsonObject> mutate)
    {
        var changed = original.DeepClone().AsObject();
        mutate(changed);
        try
        {
            JsonSerializer.Deserialize<TradingState>(changed, options);
        }
        catch (JsonException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid or incomplete stored trading JSON must be rejected.");
    }

    private static async Task RefusedSave(SqlTradingStore store, TradingState state, long expectedRevision)
    {
        try
        {
            await store.Save(state, expectedRevision, CancellationToken.None);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid trading saves must fail before opening SQL.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
