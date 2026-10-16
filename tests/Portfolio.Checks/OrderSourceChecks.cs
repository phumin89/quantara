using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class OrderSourceChecks
{
    public static async Task Run()
    {
        var store = new FakeOrderStore();
        var broker = new FakeOrderBroker();
        var time = new OrderCheckTime();
        var account = new string('a', 64);
        var create = new CreateOrderHandler(store, broker, time, new AccountContext(account));
        var source = Guid.NewGuid();
        var command = new CreateOrderCommand(
            "BTC",
            TradeSide.Buy,
            "100",
            "100",
            "Reviewed AI fixture",
            source,
            account
        );
        var first = await create.Handle(command, CancellationToken.None);
        var repeated = await create.Handle(command, CancellationToken.None);
        OrderSourceChecks.Check(
            first.Id == source && repeated.ExpiresAt == first.ExpiresAt && broker.Submissions == 0,
            "AI publishing is idempotent and never submits."
        );
        await OrderSourceChecks.Refused(create, command with { ExpectedAccountKey = new string('b', 64) });
        await OrderSourceChecks.Refused(create, command with { ExpectedAccountKey = null });
        await OrderSourceChecks.Refused(create, command with { Amount = "200" });
        var trading = new FakeTradingStore(TradingStateFactory.Create(time.GetUtcNow()));
        var guarded = new CreateOrderHandler(store, broker, time, new AccountContext(account), trading);
        foreach (var mode in new[] { TradingMode.Paper, TradingMode.Stopped, TradingMode.AutoLive })
        {
            trading.State = trading.State with { Mode = mode };
            await OrderSourceChecks.Refused(guarded, command with { SourceRunId = Guid.NewGuid() });
            await OrderSourceChecks.Refused(guarded, command);
            OrderSourceChecks.Check(
                (await store.Find(first.Id, CancellationToken.None))?.State == OrderState.Pending
                    && broker.Submissions == 0,
                "Non-manual AI publication cannot alter retained pending proposals or submit orders."
            );
        }

        trading.State = trading.State with { Mode = TradingMode.ManualReview };
        var manual = await guarded.Handle(command, CancellationToken.None);
        OrderSourceChecks.Check(
            manual.Id == first.Id && manual.ExpiresAt == first.ExpiresAt,
            "Explicit ManualReview preserves the existing immutable proposal."
        );
        broker.FailPrepare = true;
        var approve = new ApproveOrderHandler(store, broker, time);
        try
        {
            await approve.Handle(new(first.Id, true), CancellationToken.None);
            throw new InvalidOperationException("Preparation failure was expected.");
        }
        catch (HttpRequestException) { }

        OrderSourceChecks.Check(
            (await store.Find(first.Id, CancellationToken.None))?.State == OrderState.Pending
                && broker.Submissions == 0,
            "Known preparation failure cannot claim or freeze the account."
        );
        broker.FailPrepare = false;
        var accepted = await approve.Handle(new(first.Id, true), CancellationToken.None);
        OrderSourceChecks.Check(
            accepted.Status == "submitted" && broker.Submissions == 1,
            "Human approval works after preparation recovery."
        );
        Console.WriteLine(
            "Order sources: account binding, immutable idempotency, no autonomous submit and pre-send recovery passed."
        );
    }

    private static async Task Refused(CreateOrderHandler create, CreateOrderCommand command)
    {
        try
        {
            await create.Handle(command, CancellationToken.None);
        }
        catch (OrderValidationException)
        {
            return;
        }

        throw new InvalidOperationException("Changed account or source parameters must be refused.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
