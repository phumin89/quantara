using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public static class OrderStoreContractChecks
{
    private static readonly DateTimeOffset now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    public static async Task Run()
    {
        await Exposure();
        await MutableSourceStates();
        await RejectedSourceStates();
        await FailureIsolation();
    }

    private static async Task Exposure()
    {
        var store = new FakeOrderStore();
        var activeStates = new[]
        {
            OrderState.Submitting,
            OrderState.Unknown,
            OrderState.Submitted,
            OrderState.PartiallyFilled,
        };
        for (var index = 1; index <= 60; index++)
        {
            await Seed(store, Proposal(index, activeStates[(index - 1) % activeStates.Length], now.AddMinutes(-1)));
        }

        await Seed(store, Proposal(61, OrderState.Pending, now.AddTicks(1)));
        await Seed(store, Proposal(62, OrderState.Pending, now));
        await Seed(store, Proposal(63, OrderState.Pending, now.AddTicks(-1)));
        var terminalStates = new[] { OrderState.Expired, OrderState.Rejected, OrderState.Filled, OrderState.Cancelled };
        for (var index = 0; index < terminalStates.Length; index++)
        {
            await Seed(store, Proposal(64 + index, terminalStates[index], now.AddMinutes(1)));
        }

        var history = await store.List(CancellationToken.None);
        var exposed = await store.Exposure(now, CancellationToken.None);
        var expectedIds = Enumerable.Range(1, 61).Select(OrderId).ToHashSet();
        Check(
            exposed.Count == 61 && exposed.Select(proposal => proposal.Id).ToHashSet().SetEquals(expectedIds),
            "Exposure includes every active order beyond 50, ignores active expiry, and requires pending expiry after now."
        );
        var retained = await store.List(CancellationToken.None);
        Check(
            retained.Count == 67
                && retained.OrderBy(proposal => proposal.Id).SequenceEqual(history.OrderBy(proposal => proposal.Id)),
            "Exposure excludes exact-expiry and terminal orders without deleting or rewriting retained history."
        );
    }

    private static async Task MutableSourceStates()
    {
        var store = new FakeOrderStore();
        var states = new[]
        {
            OrderState.Submitting,
            OrderState.Unknown,
            OrderState.Submitted,
            OrderState.PartiallyFilled,
        };
        for (var index = 0; index < states.Length; index++)
        {
            var proposal = Proposal(100 + index, states[index], now.AddMinutes(-1), "known-broker");
            await Seed(store, proposal);
            await store.UpdateObserved(proposal.Id, OrderState.Submitted, null, "Observed", CancellationToken.None);
            var expected = proposal with
            {
                State = states[index] == OrderState.PartiallyFilled ? OrderState.PartiallyFilled : OrderState.Submitted,
                Message = "Observed",
            };
            Check(
                await store.Find(proposal.Id, CancellationToken.None) == expected,
                "A mutable observation preserves known broker identity and cannot regress a partial fill to submitted."
            );
            await store.UpdateObserved(proposal.Id, OrderState.Filled, null, "Filled", CancellationToken.None);
            Check(
                await store.Find(proposal.Id, CancellationToken.None)
                    == (expected with { State = OrderState.Filled, Message = "Filled" }),
                "Mutable orders can become terminal while retaining their known broker identity."
            );
        }
    }

    private static async Task RejectedSourceStates()
    {
        var store = new FakeOrderStore();
        var states = new[]
        {
            OrderState.Pending,
            OrderState.Expired,
            OrderState.Rejected,
            OrderState.Filled,
            OrderState.Cancelled,
        };
        for (var index = 0; index < states.Length; index++)
        {
            var proposal = Proposal(200 + index, states[index], now.AddMinutes(1), "retained-broker");
            await Seed(store, proposal);
            await Rejected(
                () =>
                    store.UpdateObserved(
                        proposal.Id,
                        OrderState.Submitted,
                        "replacement",
                        "Reopen",
                        CancellationToken.None
                    ),
                "Only submitted or uncertain orders can be reconciled."
            );
            Check(
                await store.Find(proposal.Id, CancellationToken.None) == proposal,
                "Pending and terminal source records reject observation updates without reopening or mutating history."
            );
        }
    }

    private static async Task FailureIsolation()
    {
        var store = new FakeOrderStore();
        var proposal = Proposal(300, OrderState.PartiallyFilled, now.AddMinutes(-1), "retained-broker");
        await Seed(store, proposal);
        store.FailFinish = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await store.UpdateObserved(proposal.Id, OrderState.Filled, "replacement", "Cancelled", cancellation.Token);
            throw new InvalidOperationException("A canceled observation was accepted.");
        }
        catch (OperationCanceledException) { }

        Check(
            await store.Find(proposal.Id, CancellationToken.None) == proposal,
            "Cancellation is respected before a failing observation write and preserves the exact source record."
        );
        await Rejected(
            () =>
                store.UpdateObserved(proposal.Id, OrderState.Filled, "replacement", "Failure", CancellationToken.None),
            "Fixture storage failure"
        );
        Check(
            await store.Find(proposal.Id, CancellationToken.None) == proposal,
            "FailFinish remains a no-mutation observation failure."
        );
    }

    private static Guid OrderId(int value) => new(value, 0, 0, new byte[8]);

    private static OrderProposal Proposal(
        int value,
        OrderState state,
        DateTimeOffset expiresAt,
        string? brokerId = null
    ) =>
        new(
            OrderId(value),
            "BTC",
            TradeSide.Buy,
            100m,
            100m,
            100m,
            "Order store contract fixture",
            now.AddMinutes(-5),
            expiresAt,
            state,
            brokerId,
            null
        );

    private static async Task Seed(FakeOrderStore store, OrderProposal proposal)
    {
        await store.Create(proposal with { State = OrderState.Pending }, CancellationToken.None);
        if (proposal.State != OrderState.Pending)
        {
            await store.Finish(
                proposal.Id,
                proposal.State,
                proposal.BrokerOrderId,
                proposal.Message,
                CancellationToken.None
            );
        }
    }

    private static async Task Rejected(Func<Task> update, string expectedMessage)
    {
        try
        {
            await update();
        }
        catch (InvalidOperationException error) when (error.Message == expectedMessage)
        {
            return;
        }

        throw new InvalidOperationException("The observation source or fixture failure was not rejected as expected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
