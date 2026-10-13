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
        Console.WriteLine(
            "Order recovery: exact identity, partial/filled/cancelled, unknown retention, GET signing and prepare failure passed."
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
