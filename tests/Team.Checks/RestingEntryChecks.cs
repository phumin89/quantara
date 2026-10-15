using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class RestingEntryChecks
{
    public static async Task Run()
    {
        var evidence = await new FakePortfolioSnapshotReader().Read(CancellationToken.None);
        var context = "{\"evidence\":" + evidence + "}";
        var resting = new TradeProposal(
            "BTC",
            TradeProposalSide.Buy,
            "100",
            "90",
            "Supported pullback entry",
            FakePortfolioSnapshotReader.FetchedAt,
            new TradeEntryPlan("95", "85", 60)
        );
        TradeProposalValidator.Validate(resting, context);
        foreach (
            var invalid in new[]
            {
                resting with
                {
                    EntryPlan = null,
                },
                resting with
                {
                    LimitPrice = "102",
                },
                resting with
                {
                    Amount = "701",
                },
                resting with
                {
                    EntryPlan = new TradeEntryPlan("89", "85", 60),
                },
                resting with
                {
                    EntryPlan = new TradeEntryPlan("95", "91", 60),
                },
                resting with
                {
                    EntryPlan = new TradeEntryPlan("95", "85", 0),
                },
                resting with
                {
                    EntryPlan = new TradeEntryPlan("95", "85", 1441),
                },
            }
        )
        {
            var rejected = false;
            try
            {
                TradeProposalValidator.Validate(invalid, context);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "Invalid resting entry or unavailable budget was accepted.");
        }

        var id = Guid.NewGuid();
        var handler = new PublisherHttpHandler(id);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        await new OrderProposalPublisher(http).Publish(id, new string('a', 64), resting, CancellationToken.None);
        using var published = JsonDocument.Parse(
            handler.Body ?? throw new InvalidOperationException("Missing proposal body.")
        );
        Check(
            published.RootElement.GetProperty("amount").GetString() == "100"
                && published.RootElement.GetProperty("limitPrice").GetString() == "90",
            "Exact resting amount and limit must survive publication."
        );
        var reason = published.RootElement.GetProperty("reason").GetString() ?? "";
        Check(
            reason.Contains("Target (plan): THB 95", StringComparison.Ordinal)
                && reason.Contains("Invalidation (plan): THB 85", StringComparison.Ordinal)
                && reason.Contains("Review by:", StringComparison.Ordinal),
            "The immutable human review must include the entry plan."
        );
        Check(
            handler.Request?.RequestUri?.AbsolutePath == "/api/crypto/orders",
            "A plan creates only a pending proposal."
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
