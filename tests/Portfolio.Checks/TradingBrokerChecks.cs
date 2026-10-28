using System.Text.Json;
using Quantara.Portfolio.Domain;
using Quantara.Portfolio.Domain.Trading;
using Quantara.Portfolio.Infrastructure;

namespace Portfolio.Checks;

public static class TradingBrokerChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var intent = new TradingIntent(
            Guid.NewGuid(),
            "BTC",
            TradeSide.Buy,
            100,
            100,
            100,
            now.AddMinutes(-1),
            0,
            "fake-hash",
            "fixture",
            "submitted",
            "fixture-order"
        );
        using var handler = new TradingBrokerHttpHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://bitkub.invalid/") };
        var broker = new FakeOrderBroker();
        var exchange = new BitkubTradingExchange(http, new("fake-key", "fake-secret"), broker);
        handler.Json = Evidence(intent, now);
        var observed = await exchange.Inspect(intent, CancellationToken.None);
        var fill = observed.Fills.Single();
        Check(
            observed.Status == "filled"
                && fill.Quantity == .99m
                && fill.QuantityScale == 8
                && fill.GrossThb == 99
                && fill.GrossFeeThb == 1
                && fill.FeeCreditThb == .5m
                && fill.FeeThb == .5m,
            "Signed reconciliation preserves fill amounts and actual fee credits; unused terminal budget is not spent."
        );
        foreach (
            var invalid in new[]
            {
                "identity",
                "amount",
                "rate",
                "rounded-amount",
                "rounded-rate",
                "duplicate",
                "incomplete",
                "credit",
                "fill-object",
                "fill-array",
                "fill-true",
                "fill-false",
                "fill-null",
                "fill-empty",
                "fill-whitespace",
                "fill-too-long",
            }
        )
        {
            handler.Json = Evidence(intent, now, invalid);
            var rejected = false;
            try
            {
                await exchange.Inspect(intent, CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Check(rejected, "Invalid broker evidence must fail closed: " + invalid);
        }

        foreach (
            var (scenario, expected) in new[]
            {
                ("fill-number", "42"),
                ("fill-leading-zero", "00042"),
                ("fill-boolean-string", "true"),
                ("fill-object-string", "{}"),
                ("fill-limit", new string('x', 150)),
            }
        )
        {
            handler.Json = Evidence(intent, now, scenario);
            var verified = await exchange.Inspect(intent, CancellationToken.None);
            Check(
                verified.Status == "filled" && verified.Fills.Single().BrokerFillId == expected && handler.Posts == 0,
                "Supported fill identity retains its exact text without a broker write: " + scenario
            );
        }

        const string noMatch = "No exact broker match found. Outcome remains unknown; no order was resent.";
        broker.Inspection = new(OrderState.Unknown, null, noMatch);
        var unknown = await exchange.Inspect(intent, CancellationToken.None);
        Check(
            unknown.Error == noMatch && unknown.Status == "unknown" && handler.Posts == 0,
            "The adapter preserves safe inspection diagnostics without submitting an order."
        );

        handler.Json = "{\"error\":0}";
        using var cancellation = await exchange.PrepareCancellation(intent, CancellationToken.None);
        Check(handler.Posts == 0, "Preparing a cancellation has no broker side effect.");
        Check(
            (await cancellation.Send(CancellationToken.None)).Accepted && handler.Posts == 1,
            "Cancellation dispatches exactly the signed fixture request."
        );
        using var payload = JsonDocument.Parse(handler.LastBody ?? "{}");
        Check(
            payload.RootElement.GetProperty("id").GetString() == intent.BrokerOrderId
                && payload.RootElement.GetProperty("sd").GetString() == "buy",
            "Cancellation binds the exact order and side."
        );
        var repeated = false;
        try
        {
            await cancellation.Send(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            repeated = true;
        }

        Check(repeated && handler.Posts == 1, "Prepared cancellation cannot be sent twice.");
        Console.WriteLine("Trading broker adapter checks passed with fake HTTP only.");
    }

    private static string Evidence(TradingIntent intent, DateTimeOffset now, string? scenario = null)
    {
        object? transactionId = scenario switch
        {
            "fill-object" => new { id = "fake-fill" },
            "fill-array" => new[] { "fake-fill" },
            "fill-true" => true,
            "fill-false" => false,
            "fill-null" => null,
            "fill-empty" => string.Empty,
            "fill-whitespace" => " \t\n ",
            "fill-too-long" => new string('x', 151),
            "fill-number" => 42,
            "fill-leading-zero" => "00042",
            "fill-boolean-string" => "true",
            "fill-object-string" => "{}",
            "fill-limit" => new string('x', 150),
            _ => "fake-fill",
        };
        var row = new
        {
            txn_id = transactionId,
            amount = 99m,
            rate = 100m,
            fee = 1m,
            credit = scenario == "credit" ? 2m : .5m,
            timestamp = now.ToUnixTimeMilliseconds(),
        };
        return JsonSerializer.Serialize(
            new
            {
                error = 0,
                result = new
                {
                    id = scenario == "identity" ? "other" : intent.BrokerOrderId,
                    client_id = intent.Id.ToString("N"),
                    amount = scenario switch
                    {
                        "amount" => "101",
                        "rounded-amount" => "100.000000000000000000000000001",
                        _ => "100",
                    },
                    rate = scenario switch
                    {
                        "rate" => "101",
                        "rounded-rate" => "1.00000000000000000000000000001e2",
                        _ => "100",
                    },
                    filled = scenario == "incomplete" ? 100m : 99m,
                    remaining = 0,
                    status = "filled",
                    partial_filled = false,
                    history = scenario == "duplicate" ? new[] { row, row } : new[] { row },
                },
            }
        );
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
