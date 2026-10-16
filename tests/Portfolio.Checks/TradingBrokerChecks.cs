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
        var exchange = new BitkubTradingExchange(http, new("fake-key", "fake-secret"), new FakeOrderBroker());
        handler.Json = Evidence(intent, now);
        var observed = await exchange.Inspect(intent, CancellationToken.None);
        var fill = observed.Fills.Single();
        Check(
            observed.Status == "filled"
                && fill.Quantity == .99m
                && fill.GrossThb == 99
                && fill.GrossFeeThb == 1
                && fill.FeeCreditThb == .5m
                && fill.FeeThb == .5m,
            "Signed reconciliation preserves fill amounts and actual fee credits; unused terminal budget is not spent."
        );
        foreach (var invalid in new[] { "identity", "amount", "duplicate", "incomplete", "credit" })
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

    private static string Evidence(TradingIntent intent, DateTimeOffset now, string? invalid = null)
    {
        var row = new
        {
            txn_id = "fake-fill",
            amount = 99m,
            rate = 100m,
            fee = 1m,
            credit = invalid == "credit" ? 2m : .5m,
            timestamp = now.ToUnixTimeMilliseconds(),
        };
        return JsonSerializer.Serialize(
            new
            {
                error = 0,
                result = new
                {
                    id = invalid == "identity" ? "other" : intent.BrokerOrderId,
                    client_id = intent.Id.ToString("N"),
                    amount = invalid == "amount" ? 101 : 100,
                    filled = invalid == "incomplete" ? 100m : 99m,
                    remaining = 0,
                    status = "filled",
                    partial_filled = false,
                    history = invalid == "duplicate" ? new[] { row, row } : new[] { row },
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
