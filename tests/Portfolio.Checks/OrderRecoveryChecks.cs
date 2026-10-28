using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderRecoveryChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var proposal = new OrderProposal(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100,
            100,
            100,
            "Recovery fixture",
            now,
            now.AddMinutes(3),
            OrderState.Unknown,
            null,
            null
        );
        using var handler = new InspectionHttpHandler { ClientId = proposal.Id.ToString("N") };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var inspector = new BitkubOrderInspector(http, new BitkubCredentials("fake-key", "fake-secret"));
        var observed = await inspector.Inspect(proposal, CancellationToken.None);
        OrderRecoveryChecks.Check(
            observed.State == OrderState.Filled && observed.BrokerOrderId == "order-1",
            "Exact client_id recovers a filled order."
        );
        handler.Status = "unfilled";
        handler.Partial = true;
        OrderRecoveryChecks.Check(
            (await inspector.Inspect(proposal, CancellationToken.None)).State == OrderState.PartiallyFilled,
            "Partial fill is distinct from submitted."
        );
        handler.Status = "cancelled";
        OrderRecoveryChecks.Check(
            (await inspector.Inspect(proposal, CancellationToken.None)).State == OrderState.Cancelled,
            "Cancelled state is verified by broker."
        );
        handler.Match = false;
        OrderRecoveryChecks.Check(
            (await inspector.Inspect(proposal, CancellationToken.None)).State == OrderState.Unknown,
            "No exact match leaves unknown outcome blocked."
        );
        OrderRecoveryChecks.Check(handler.Posts == 0, "Reconciliation sends signed GET only.");
        handler.FailTime = true;
        var broker = new BitkubOrderBroker(
            http,
            new BitkubCredentials("fake-key", "fake-secret"),
            new BitkubAccountReader(http, new BitkubCredentials("fake-key", "fake-secret"))
        );
        try
        {
            await broker.Prepare(proposal, CancellationToken.None);
            throw new InvalidOperationException("Expected preparation failure.");
        }
        catch (HttpRequestException)
        {
            OrderRecoveryChecks.Check(handler.Posts == 0, "Failed timestamp preparation cannot send an order.");
        }

        OrderRecoveryChecks.Check((int)OrderState.Unknown == 5, "Persisted legacy unknown enum value is preserved.");
        var store = new FakeOrderStore();
        await store.Create(proposal, CancellationToken.None);
        var fakeBroker = new FakeOrderBroker();
        var reconcile = new ReconcileOrderHandler(store, fakeBroker, TimeProvider.System);
        await reconcile.Handle(new ReconcileOrderCommand(proposal.Id), CancellationToken.None);
        OrderRecoveryChecks.Check(
            (await store.Find(proposal.Id, CancellationToken.None))?.State == OrderState.Filled
                && fakeBroker.Submissions == 0,
            "Reconciliation persists broker evidence without submission."
        );
        fakeBroker.Inspection = new(OrderState.Unknown, null, "No match");
        await reconcile.Handle(new ReconcileOrderCommand(proposal.Id), CancellationToken.None);
        OrderRecoveryChecks.Check(
            (await store.Find(proposal.Id, CancellationToken.None))?.State == OrderState.Filled,
            "Terminal outcome cannot be overwritten by later reconciliation."
        );
        await OrderRecoveryChecks.ExactOrderTerms(proposal);
        await OrderRecoveryChecks.IncompleteFillsRetainUnknown();
        Console.WriteLine(
            "Order recovery: exact identity, partial/filled/cancelled, unknown retention, GET signing and prepare failure passed."
        );
    }

    private static async Task ExactOrderTerms(OrderProposal template)
    {
        foreach (var side in new[] { TradeSide.Buy, TradeSide.Sell })
        {
            var proposal = template with { Side = side, BrokerOrderId = "order-1" };
            foreach (
                var (amount, rate) in new[]
                {
                    ("99", "100"),
                    ("101", "100"),
                    ("100", "99"),
                    ("100", "101"),
                    ("100.000000000000000000000000001", "100"),
                    ("100", "100.000000000000000000000000001"),
                    ("1.00000000000000000000000000001e2", "100"),
                    ("100", "1.00000000000000000000000000001e2"),
                    ("\"1e2\\u0000\"", "100"),
                    ("100", "\"1e2\\u0000\""),
                }
            )
            {
                using var handler = new InspectionHttpHandler
                {
                    ClientId = proposal.Id.ToString("N"),
                    OrderAmountJson = amount,
                    OrderRateJson = rate,
                    Status = "cancelled",
                    HistoryJson = "[]",
                };
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
                var credentials = new BitkubCredentials("fake-key", "fake-secret");
                var broker = new BitkubOrderBroker(http, credentials, new BitkubAccountReader(http, credentials));
                var store = new FakeOrderStore();
                await store.Create(proposal, CancellationToken.None);
                try
                {
                    await new ReconcileOrderHandler(store, broker, TimeProvider.System).Handle(
                        new(proposal.Id),
                        CancellationToken.None
                    );
                    throw new InvalidOperationException("Inconsistent broker terms cleared an unknown order.");
                }
                catch (InvalidDataException error)
                    when (error.Message == "Broker order identity did not match the proposal.") { }

                OrderRecoveryChecks.Check(
                    await store.Find(proposal.Id, CancellationToken.None) == proposal && handler.Posts == 0,
                    "An inconsistent GET must preserve the complete unknown proposal and never send an order."
                );
            }

            foreach (
                var (amount, rate, amountJson, rateJson) in new[]
                {
                    (100m, 100m, "1e2", "\"+1.00E+2\""),
                    (0.0000000000000000000000000001m, 0.0000000000000000000000000001m, "\"1e-28\"", "1e-28"),
                    (12345678901.234567890123456789m, 1m, "12345678901.234567890123456789", "\"1.000\""),
                }
            )
            {
                using var handler = new InspectionHttpHandler
                {
                    OrderAmountJson = amountJson,
                    OrderRateJson = rateJson,
                    Status = "cancelled",
                    HistoryJson = "[]",
                };
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
                var observed = await new BitkubOrderInspector(http, new("fake-key", "fake-secret")).Inspect(
                    proposal with
                    {
                        Amount = amount,
                        LimitPrice = rate,
                    },
                    CancellationToken.None
                );
                OrderRecoveryChecks.Check(
                    observed.State == OrderState.Cancelled && handler.Posts == 0,
                    "Exact scientific notation, scale 28 and representable 29-digit values remain valid broker terms."
                );
            }

            foreach (
                var (status, partial, expected, fillAmount, filled, remaining) in new[]
                {
                    ("cancelled", false, OrderState.Cancelled, 0m, "0", "100"),
                    ("unfilled", false, OrderState.Submitted, 0m, "0", "100"),
                    ("unfilled", true, OrderState.PartiallyFilled, 1m, "1", "99"),
                    ("filled", false, OrderState.Filled, 100m, "100", "0"),
                }
            )
            {
                var history =
                    partial || status == "filled"
                        ? JsonSerializer.Serialize(
                            new[]
                            {
                                new
                                {
                                    txn_id = "exact-terms-fill",
                                    amount = fillAmount,
                                    rate = 100m,
                                    fee = 0m,
                                    credit = 0m,
                                    timestamp = template.CreatedAt.ToUnixTimeMilliseconds(),
                                },
                            }
                        )
                        : "[]";
                using var handler = new InspectionHttpHandler
                {
                    ClientId = "",
                    OrderAmountJson = "\"000100.000\"",
                    OrderRateJson = "100.000",
                    Status = status,
                    Partial = partial,
                    OrderFilledJson = filled,
                    OrderRemainingJson = remaining,
                    HistoryJson = history,
                };
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
                var observed = await new BitkubOrderInspector(http, new("fake-key", "fake-secret")).Inspect(
                    proposal,
                    CancellationToken.None
                );
                OrderRecoveryChecks.Check(
                    observed.State == expected && observed.BrokerOrderId == "order-1" && handler.Posts == 0,
                    "Exact numeric terms preserve supported outcomes and a legacy empty client reference for a known broker ID."
                );
            }

            using var linkedHandler = new InspectionHttpHandler
            {
                LinkedChild = true,
                Status = "cancelled",
                HistoryJson = "[]",
            };
            using var linkedHttp = new HttpClient(linkedHandler) { BaseAddress = new Uri("https://fixture.invalid/") };
            var linked = await new BitkubOrderInspector(linkedHttp, new("fake-key", "fake-secret")).Inspect(
                proposal with
                {
                    BrokerOrderId = "order-child",
                },
                CancellationToken.None
            );
            OrderRecoveryChecks.Check(
                linked.State == OrderState.Cancelled
                    && linked.BrokerOrderId == "order-1"
                    && linkedHandler.OrderInfoReads == 2
                    && linkedHandler.Posts == 0,
                "A child amount of 25 still resolves through first to the original amount of 100 before term validation, without a broker write."
            );

            linkedHandler.WrongChildIdentity = true;
            var unknown = proposal with { BrokerOrderId = "order-child" };
            var unknownStore = new FakeOrderStore();
            await unknownStore.Create(unknown, CancellationToken.None);
            var linkedCredentials = new BitkubCredentials("fake-key", "fake-secret");
            var linkedBroker = new BitkubOrderBroker(
                linkedHttp,
                linkedCredentials,
                new BitkubAccountReader(linkedHttp, linkedCredentials)
            );
            try
            {
                await new ReconcileOrderHandler(unknownStore, linkedBroker, TimeProvider.System).Handle(
                    new(unknown.Id),
                    CancellationToken.None
                );
                throw new InvalidOperationException("An unrelated child response supplied trusted order lineage.");
            }
            catch (InvalidDataException error)
                when (error.Message == "Broker order identity did not match the proposal.") { }

            OrderRecoveryChecks.Check(
                await unknownStore.Find(unknown.Id, CancellationToken.None) == unknown
                    && linkedHandler.OrderInfoReads == 3
                    && linkedHandler.Posts == 0,
                "A wrong child ID cannot trigger a follow-up read or clear the complete unknown proposal."
            );
        }
    }

    private static async Task IncompleteFillsRetainUnknown()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1702466376000);
        var proposal = new OrderProposal(
            Guid.Parse("25565e43-79cd-49f4-b77c-19cc488aa5d1"),
            "BTC",
            TradeSide.Buy,
            100m,
            100m,
            100m,
            "Incomplete history fixture",
            now.AddSeconds(-2),
            now.AddMinutes(3),
            OrderState.Unknown,
            "order-1",
            null
        );
        using var handler = new InspectionHttpHandler
        {
            ClientId = proposal.Id.ToString("N"),
            OrderFilledJson = "100",
            OrderRemainingJson = "0",
            HistoryJson =
                "[{\"txn_id\":\"incomplete-recovery\",\"amount\":\"1\",\"rate\":\"100\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]",
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        var credentials = new BitkubCredentials("fake-key", "fake-secret");
        var broker = new BitkubOrderBroker(http, credentials, new BitkubAccountReader(http, credentials));
        var store = new FakeOrderStore();
        await store.Create(proposal, CancellationToken.None);
        try
        {
            await new ReconcileOrderHandler(store, broker, new FixedTimeProvider(now)).Handle(
                new(proposal.Id),
                CancellationToken.None
            );
            throw new InvalidOperationException("Incomplete fill history cleared an unknown order.");
        }
        catch (InvalidDataException error)
            when (error.Message == "Broker fill history does not cover the reported filled amount.") { }

        OrderRecoveryChecks.Check(
            await store.Find(proposal.Id, CancellationToken.None) == proposal && handler.Posts == 0,
            "Incomplete fill evidence preserves the complete unknown proposal and never submits an order."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
