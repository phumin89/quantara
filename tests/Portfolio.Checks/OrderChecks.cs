using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderChecks
{
    public static async Task Run()
    {
        var time = new OrderCheckTime();
        var store = new FakeOrderStore();
        var broker = new FakeOrderBroker();
        var create = new CreateOrderHandler(store, broker, time);
        var approve = new ApproveOrderHandler(store, broker, time);
        var draft = await create.Handle(
            new("BTC", TradeSide.Buy, "100.00", "100.00", "Manual review fixture"),
            CancellationToken.None
        );
        OrderChecks.Check(
            broker.Submissions == 0
                && draft.Status == "pending"
                && draft.ExpiresAt == time.Now.AddMinutes(3)
                && draft.EstimatedFeeThb == "0.25",
            "draft never submits and expires after three minutes"
        );
        await OrderChecks.Blocked(() => approve.Handle(new(draft.Id, false), CancellationToken.None).AsTask());
        broker.Available = 0;
        await OrderChecks.Blocked(() => approve.Handle(new(draft.Id, true), CancellationToken.None).AsTask());
        broker.Available = 1000;
        broker.Price = 102;
        await OrderChecks.Blocked(() => approve.Handle(new(draft.Id, true), CancellationToken.None).AsTask());
        OrderChecks.Check(
            broker.Submissions == 0,
            "no confirmation, insufficient funds and changed price cannot submit"
        );
        broker.Price = 100;
        broker.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = approve.Handle(new(draft.Id, true), CancellationToken.None).AsTask();
        var duplicate = await approve.Handle(new(draft.Id, true), CancellationToken.None);
        await new RejectOrderHandler(store, time).Handle(new(draft.Id), CancellationToken.None);
        OrderChecks.Check(
            duplicate.Status == "submitting" && broker.Submissions == 1,
            "durable claim blocks duplicate approval and late rejection"
        );
        broker.Release.SetResult();
        var submitted = await first;
        var repeated = await approve.Handle(new(draft.Id, true), CancellationToken.None);
        OrderChecks.Check(
            submitted.Status == "submitted" && repeated.BrokerOrderId == "broker-123" && broker.Submissions == 1,
            "accepted order is not resent"
        );
        broker.Release = null;
        var expired = await create.Handle(
            new("BTC", TradeSide.Sell, "0.2", "100", "Expiry fixture"),
            CancellationToken.None
        );
        time.Now = expired.ExpiresAt;
        await OrderChecks.Blocked(() => approve.Handle(new(expired.Id, true), CancellationToken.None).AsTask());
        var views = await new GetOrdersHandler(store, time).Handle(new(), CancellationToken.None);
        OrderChecks.Check(
            views.Single(view => view.Id == expired.Id).Status == "expired" && broker.Submissions == 1,
            "server deadline is authoritative with no timeout approval"
        );
        var preflight = await create.Handle(
            new("BTC", TradeSide.Buy, "100", "100", "Preflight expiry fixture"),
            CancellationToken.None
        );
        broker.AdvanceOnBalance = time;
        await OrderChecks.Blocked(() => approve.Handle(new(preflight.Id, true), CancellationToken.None).AsTask());
        broker.AdvanceOnBalance = null;
        OrderChecks.Check(broker.Submissions == 1, "expiry during preflight cannot submit");
        var uncertain = await create.Handle(
            new("BTC", TradeSide.Buy, "100", "100", "Uncertain fixture"),
            CancellationToken.None
        );
        broker.FailSubmit = true;
        var unknown = await approve.Handle(new(uncertain.Id, true), CancellationToken.None);
        await approve.Handle(new(uncertain.Id, true), CancellationToken.None);
        var other = await create.Handle(
            new("BTC", TradeSide.Buy, "100", "100", "Second proposal"),
            CancellationToken.None
        );
        await OrderChecks.Blocked(() => approve.Handle(new(other.Id, true), CancellationToken.None).AsTask());
        OrderChecks.Check(
            unknown.Status == "unknown" && broker.Submissions == 2,
            "unknown outcome never retries and blocks further account submission"
        );
        await OrderChecks.StorageFailure();
        await OrderChecks.Transport();
        Console.WriteLine("Order checks passed without broker secrets or real order calls.");
    }

    private static async Task StorageFailure()
    {
        var time = new OrderCheckTime();
        var store = new FakeOrderStore { FailFinish = true };
        var broker = new FakeOrderBroker();
        var draft = await new CreateOrderHandler(store, broker, time).Handle(
            new("BTC", TradeSide.Buy, "100", "100", "Storage fixture"),
            CancellationToken.None
        );
        try
        {
            await new ApproveOrderHandler(store, broker, time).Handle(new(draft.Id, true), CancellationToken.None);
            throw new InvalidOperationException("Expected storage failure");
        }
        catch (InvalidOperationException error) when (error.Message == "Fixture storage failure") { }

        var restart = await new ApproveOrderHandler(store, broker, time).Handle(
            new(draft.Id, true),
            CancellationToken.None
        );
        OrderChecks.Check(
            restart.Status == "submitting" && broker.Submissions == 1,
            "outcome persistence failure and restart cannot resubmit"
        );
    }

    private static async Task Transport()
    {
        using var handler = new OrderBrokerHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake-bitkub.invalid") };
        var credentials = new BitkubCredentials("fake-key", "fake-secret");
        var broker = new BitkubOrderBroker(http, credentials, new BitkubAccountReader(http, credentials));
        var market = await broker.Market("BTC", TradeSide.Buy, CancellationToken.None);
        OrderChecks.Check(
            market.ReferencePrice == 100m && market.Enabled,
            "THB spot rules and ticker are read freshly"
        );
        var now = DateTimeOffset.UtcNow;
        var proposal = new OrderProposal(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100.00m,
            100.00m,
            100m,
            "Fixture",
            now,
            now.AddMinutes(3),
            OrderState.Pending,
            null,
            null
        );
        await broker.Submit(proposal, CancellationToken.None);
        await broker.Submit(
            proposal with
            {
                Id = Guid.NewGuid(),
                Side = TradeSide.Sell,
                Amount = 0.200m,
            },
            CancellationToken.None
        );
        using var buy = JsonDocument.Parse(handler.Bodies[0]);
        using var sell = JsonDocument.Parse(handler.Bodies[1]);
        OrderChecks.Check(
            buy.RootElement.GetProperty("amt").GetDecimal() == 100m
                && sell.RootElement.GetProperty("amt").GetDecimal() == 0.2m
                && buy.RootElement.GetProperty("typ").GetString() == "limit",
            "buy uses THB and sell uses base quantity, both fixed limit orders"
        );
        OrderChecks.Check(
            !handler.Bodies[0].Contains("100.00", StringComparison.Ordinal)
                && buy.RootElement.GetProperty("client_id").GetString() == proposal.Id.ToString("N"),
            "signed exact POST body strips trailing zeros and includes stable client reference"
        );
        OrderChecks.Check(
            handler.Paths.Count(path => path.Contains("place-bid", StringComparison.Ordinal)) == 1
                && handler.Paths.Count(path => path.Contains("place-ask", StringComparison.Ordinal)) == 1,
            "no hidden mutation endpoints or automatic retry"
        );
    }

    private static async Task Blocked(Func<Task<OrderView>> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException("An unsafe order was accepted");
        }
        catch (OrderValidationException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
