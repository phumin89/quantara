using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class RiskFillChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(1702466376000);
        var proposal = new OrderProposal(
            Guid.Parse("4ae368f0-80c1-4c9a-8541-d841cfac19c4"),
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
            OrderAmountJson = "\"1000\"",
            OrderFilledJson = "\"250\"",
            OrderRemainingJson = "\"750\"",
            Status = "unfilled",
            Partial = true,
            HistoryJson =
                "[{\"txn_id\":\"txn-1\",\"amount\":\"250\",\"rate\":\"100\",\"fee\":\"0.625\",\"credit\":\"0.1\",\"timestamp\":1702466375000}]",
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var inspector = new BitkubOrderInspector(http, new("fake-key", "fake-secret"), new FixedTimeProvider(now));
        var observed = await inspector.Inspect(proposal, CancellationToken.None);
        var fill = observed.Fills?.Single() ?? throw new InvalidOperationException("Missing verified fill.");
        Check(
            observed.State == OrderState.PartiallyFilled
                && fill.Quantity == 2.5m
                && fill.Price == 100
                && fill.FeeThb == .625m
                && fill.FeeCreditThb == .1m,
            "Buy fill converts quoted THB to base and preserves actual fees and credit."
        );
        observed = await inspector.Inspect(proposal with { Side = TradeSide.Sell }, CancellationToken.None);
        Check(observed.Fills?.Single().Quantity == 250, "Sell fill preserves base units.");
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-buy-above-limit\",\"amount\":\"250\",\"rate\":\"101\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "Buy fill above the reviewed limit is rejected."
        );
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-sell-below-limit\",\"amount\":\"250\",\"rate\":\"99\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        await Rejects(
            () => inspector.Inspect(proposal with { Side = TradeSide.Sell }, CancellationToken.None),
            "Sell fill below the reviewed limit is rejected."
        );
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-buy-below-limit\",\"amount\":\"250\",\"rate\":\"99\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        observed = await inspector.Inspect(proposal, CancellationToken.None);
        Check(
            observed.Fills?.Single() is { Quantity: 250m / 99m, Price: 99m },
            "Buy fill below the reviewed limit preserves exact quote conversion and price."
        );
        handler.HistoryJson =
            "[{\"txn_id\":\"txn-sell-above-limit\",\"amount\":\"250\",\"rate\":\"101\",\"fee\":\"0\",\"credit\":\"0\",\"timestamp\":1702466375000}]";
        observed = await inspector.Inspect(proposal with { Side = TradeSide.Sell }, CancellationToken.None);
        Check(
            observed.Fills?.Single() is { Quantity: 250m, Price: 101m },
            "Sell fill above the reviewed limit preserves exact base quantity and price."
        );
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
        await RiskFillChecks.Coverage(proposal, now);
        Console.WriteLine(
            "Risk fills: exact identity, reported coverage, quote/base units and unknown fee evidence passed."
        );
    }

    private static async Task Coverage(OrderProposal proposal, DateTimeOffset now)
    {
        using var handler = new InspectionHttpHandler
        {
            ClientId = proposal.Id.ToString("N"),
            OrderAmountJson = "1000",
            OrderFilledJson = "250",
            OrderRemainingJson = "750",
            Status = "unfilled",
            Partial = true,
            HistoryJson =
                "[{\"txn_id\":\"coverage-partial\",\"amount\":\"250\",\"rate\":\"100\",\"fee\":\"0.625\",\"credit\":\"0.1\",\"timestamp\":1702466375000}]",
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var inspector = new BitkubOrderInspector(http, new("fake-key", "fake-secret"), new FixedTimeProvider(now));
        foreach (var filled in new[] { "250", "\"250.525\"", "250.00000001" })
        {
            handler.OrderFilledJson = filled;
            Check(
                (await inspector.Inspect(proposal, CancellationToken.None)).State == OrderState.PartiallyFilled,
                "Buy coverage accepts gross, gross plus net fees and the existing 1e-8 tolerance."
            );
        }

        foreach (var filled in new[] { "251", "249", "250.625", "250.00000002" })
        {
            handler.OrderFilledJson = filled;
            await Rejects(
                () => inspector.Inspect(proposal, CancellationToken.None),
                "Incomplete or surplus fill history, gross fees without credit and excess tolerance are rejected."
            );
        }

        foreach (var field in new[] { "filled", "remaining" })
        {
            foreach (var invalid in new[] { "\"not-a-number\"", "null", "true", "-1", "-0.000000001" })
            {
                handler.OrderFilledJson = field == "filled" ? invalid : "250";
                handler.OrderRemainingJson = field == "remaining" ? invalid : "750";
                await Rejects(
                    () => inspector.Inspect(proposal, CancellationToken.None),
                    "Malformed or negative " + field + " must not become verified fill evidence."
                );
            }
        }

        handler.OrderFilledJson = "250";
        handler.OrderRemainingJson = "1001";
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "Remaining amount cannot exceed the reviewed proposal."
        );
        handler.Status = "filled";
        handler.Partial = false;
        handler.OrderFilledJson = "1000";
        handler.OrderRemainingJson = "0";
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "A Filled result cannot terminalize an order with incomplete history."
        );
        handler.OrderRemainingJson = "1";
        handler.HistoryJson =
            "[{\"txn_id\":\"coverage-complete\",\"amount\":\"1000\",\"rate\":\"100\",\"fee\":\"0.625\",\"credit\":\"0.1\",\"timestamp\":1702466375000}]";
        await Rejects(
            () => inspector.Inspect(proposal, CancellationToken.None),
            "Filled status requires exactly zero remaining amount."
        );
        handler.OrderRemainingJson = "0";
        foreach (
            var (side, filled, quantity) in new[]
            {
                (TradeSide.Buy, "1000", 10m),
                (TradeSide.Buy, "1000.525", 10m),
                (TradeSide.Sell, "1000", 1000m),
            }
        )
        {
            handler.OrderFilledJson = filled;
            var observed = await inspector.Inspect(proposal with { Side = side }, CancellationToken.None);
            Check(
                observed.State == OrderState.Filled
                    && observed.Fills?.Single() is { Price: 100m, FeeThb: .625m, FeeCreditThb: .1m } fill
                    && fill.Quantity == quantity,
                "Complete buy/sell history preserves exact quantity, gross fees and fee credit."
            );
        }

        handler.OrderFilledJson = "1000.525";
        await Rejects(
            () => inspector.Inspect(proposal with { Side = TradeSide.Sell }, CancellationToken.None),
            "Sell base-unit coverage cannot include THB fees."
        );
        handler.HistoryJson = null;
        handler.OrderFilledJson = "\"legacy-unavailable\"";
        handler.OrderRemainingJson = "\"legacy-unavailable\"";
        var legacy = await inspector.Inspect(proposal, CancellationToken.None);
        Check(
            legacy.State == OrderState.Filled && legacy.Fills is null,
            "Legacy nonsizing inspection without history retains its existing outcome and unknown fills."
        );
        Check(handler.Posts == 0, "Reported fill coverage inspection never sends a broker mutation.");
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
