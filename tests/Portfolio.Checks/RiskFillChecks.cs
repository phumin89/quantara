using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class RiskFillChecks
{
    public static async Task Run()
    {
        var proposal = new OrderProposal(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            1000,
            100,
            100,
            "Fixture",
            DateTimeOffset.FromUnixTimeMilliseconds(1702466374000),
            DateTimeOffset.FromUnixTimeMilliseconds(1702466376000),
            OrderState.Submitted,
            "order-1",
            null
        );
        using var handler = new InspectionHttpHandler
        {
            ClientId = proposal.Id.ToString("N"),
            HistoryJson =
                "[{\"txn_id\":\"txn-1\",\"amount\":\"250\",\"rate\":\"100\",\"fee\":\"0.625\",\"credit\":\"0.1\",\"timestamp\":1702466375000}]",
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var inspector = new BitkubOrderInspector(http, new("fake-key", "fake-secret"));
        var observed = await inspector.Inspect(proposal, CancellationToken.None);
        var fill = observed.Fills?.Single() ?? throw new InvalidOperationException("Missing verified fill.");
        Check(
            fill.Quantity == 2.5m && fill.Price == 100 && fill.FeeThb == .625m && fill.FeeCreditThb == .1m,
            "Buy fill converts quoted THB to base and preserves actual fees and credit."
        );
        observed = await inspector.Inspect(proposal with { Side = TradeSide.Sell }, CancellationToken.None);
        Check(observed.Fills?.Single().Quantity == 250, "Sell fill preserves base units.");
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-1\",\"amount\":\"250\",\"rate\":\"100\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        await Rejects(() => inspector.Inspect(proposal, CancellationToken.None), "Missing fee is Unknown, never zero.");
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-1\",\"amount\":\"1001\",\"rate\":\"100\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "Fill cannot exceed the reviewed amount."
        );
        handler.Match = false;
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "Identity is checked before accepting fills."
        );
        Check(handler.Posts == 0, "Fill auditing never sends a broker mutation.");
        Console.WriteLine("Risk fills: exact identity, quote/base units and unknown fee evidence passed.");
    }

    private static async Task Rejects(
        Func<Task<Quantara.Portfolio.Application.OrderInspection>> operation,
        string message
    )
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is InvalidDataException or KeyNotFoundException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
