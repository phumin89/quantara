using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class AdaptiveSizingChecks
{
    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var proposal = new TradeProposal(
            "BTC",
            TradeProposalSide.Buy,
            "999999",
            "90",
            "Adaptive setup",
            now,
            new TradeEntryPlan("110", "80", 15)
        );
        var context = Context("aggressive", new string('a', 64), now, 100m);
        TradeProposalValidator.Validate(proposal, context);

        var disabled = Context("disabled", null, now, 100m);
        CheckRejects(() => TradeProposalValidator.Validate(proposal, disabled), "disabled sizing keeps amount cap");
        TradeProposalValidator.Validate(proposal with { Amount = "1" }, disabled);
        CheckRejects(
            () => TradeProposalValidator.Validate(proposal, Context("unknown", null, now, 1000000m)),
            "unknown sizing profile rejected"
        );
        CheckRejects(
            () => TradeProposalValidator.Validate(proposal with { EntryPlan = null }, context),
            "aggressive sizing requires entry plan"
        );
        CheckRejects(
            () => TradeProposalValidator.Validate(proposal, Context("aggressive", "bad", now, 1000000m)),
            "malformed policy hash rejected"
        );
        CheckRejects(
            () => TradeProposalValidator.Validate(proposal, Context("aggressive", null, now, 1000000m)),
            "missing policy hash rejected"
        );
        CheckRejects(
            () => TradeProposalValidator.Validate(proposal, Context("aggressive", new string('a', 64), now, 0m)),
            "zero available funds rejected"
        );
        var sell = proposal with
        {
            Side = TradeProposalSide.Sell,
            EntryPlan = null,
            Amount = "999999",
            LimitPrice = "100",
        };
        TradeProposalValidator.Validate(sell, Context("aggressive", new string('a', 64), now, 1000m, 1000000m));

        var buyRunId = Guid.NewGuid();
        var buyHandler = new PublisherHttpHandler(buyRunId);
        using var buyHttp = new HttpClient(buyHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        await new OrderProposalPublisher(buyHttp).Publish(
            buyRunId,
            new string('a', 64),
            proposal,
            CancellationToken.None
        );
        using var buyJson = JsonDocument.Parse(
            buyHandler.Body ?? throw new InvalidOperationException("buy payload missing")
        );
        Check(
            buyJson.RootElement.GetProperty("riskSignal").GetProperty("stopPrice").GetDecimal() == 80m,
            "buy risk signal uses invalidation"
        );
        Check(
            buyJson.RootElement.GetProperty("riskSignal").GetProperty("strategyEvidence").GetString()
                == "reviewer-entry-plan",
            "buy risk signal source"
        );

        var sellRunId = Guid.NewGuid();
        var sellHandler = new PublisherHttpHandler(sellRunId);
        using var sellHttp = new HttpClient(sellHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        await new OrderProposalPublisher(sellHttp).Publish(
            sellRunId,
            new string('a', 64),
            proposal with
            {
                Side = TradeProposalSide.Sell,
                EntryPlan = null,
            },
            CancellationToken.None
        );
        using var sellJson = JsonDocument.Parse(
            sellHandler.Body ?? throw new InvalidOperationException("sell payload missing")
        );
        Check(
            sellJson.RootElement.GetProperty("riskSignal").GetProperty("stopPrice").ValueKind == JsonValueKind.Null,
            "sell risk signal has no stop price"
        );
        Check(
            sellJson.RootElement.GetProperty("riskSignal").GetProperty("strategyEvidence").GetString()
                == "reviewer-exit-thesis",
            "sell risk signal source"
        );
        Console.WriteLine("Adaptive sizing and risk signal checks passed.");
    }

    private static string Context(
        string profile,
        string? hash,
        DateTimeOffset observedAt,
        decimal available,
        decimal btcAvailable = 0m
    )
    {
        var hashJson = hash is null ? "" : $",\"riskPolicyHash\":\"{hash}\"";
        var btc =
            btcAvailable > 0 ? $",{{\"symbol\":\"BTC\",\"quantity\":{btcAvailable},\"available\":{btcAvailable}}}" : "";
        return $"{{\"evidence\":{{\"accountKey\":\"{new string('a', 64)}\",\"walletEvidence\":{{\"accountKey\":\"{new string('a', 64)}\",\"observedAt\":\"{observedAt:O}\",\"sizingProfile\":\"{profile}\"{hashJson},\"balances\":[{{\"symbol\":\"THB\",\"quantity\":{available},\"available\":{available}}}{btc}]}},\"marketResearch\":{{\"available\":true,\"fetchedAt\":\"{observedAt:O}\",\"markets\":[{{\"symbol\":\"BTC_THB\",\"last\":100,\"bid\":99,\"ask\":101}}]}}}}}}";
    }

    private static void CheckRejects(Action action, string name)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        if (!rejected)
        {
            throw new InvalidOperationException(name);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }
    }
}
