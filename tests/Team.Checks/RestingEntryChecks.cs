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
        RestingEntryChecks.ExactInputPrecision();
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

    private static void ExactInputPrecision()
    {
        var now = DateTimeOffset.UtcNow;
        var context = JsonSerializer.Serialize(
            new
            {
                evidence = new
                {
                    accountKey = new string('a', 64),
                    walletEvidence = new
                    {
                        accountKey = new string('a', 64),
                        observedAt = now,
                        sizingProfile = "disabled",
                        balances = new[] { new { symbol = "THB", available = 1000000000000m } },
                    },
                    marketResearch = new
                    {
                        available = true,
                        fetchedAt = now,
                        markets = new[]
                        {
                            new
                            {
                                symbol = "BTC_THB",
                                last = 200000000000m,
                                bid = 199999999999m,
                                ask = 200000000001m,
                            },
                        },
                    },
                },
            }
        );
        var proposal = new TradeProposal(
            "BTC",
            TradeProposalSide.Buy,
            "100",
            "200000000000",
            "Synthetic exact decimal plan",
            now,
            new TradeEntryPlan("300000000000", "100000000000", 60)
        );
        TradeProposalValidator.Validate(proposal, context);
        foreach (
            var invalid in new[]
            {
                proposal with
                {
                    Amount = "123456789012.000000000000000001",
                },
                proposal with
                {
                    LimitPrice = "200000000000.000000000000000001",
                },
                proposal with
                {
                    EntryPlan = new("300000000000.000000000000000001", "100000000000", 60),
                },
                proposal with
                {
                    EntryPlan = new("300000000000", "100000000000.000000000000000001", 60),
                },
            }
        )
        {
            try
            {
                TradeProposalValidator.Validate(invalid, context);
                throw new InvalidOperationException("A rounded research proposal field was accepted.");
            }
            catch (InvalidOperationException error)
                when (error.Message
                    == "Trade proposal amount or price must fit supported decimal precision without rounding."
                ) { }
        }

        foreach (
            var exact in new[]
            {
                ".5",
                "000100.000",
                "100.",
                "12345678901.234567890123456789",
                "0.0000000000000000000000000001",
                "1000000000000",
            }
        )
        {
            TradeProposalValidator.Validate(proposal with { Amount = exact }, context);
        }

        TradeProposalValidator.Validate(
            proposal with
            {
                LimitPrice = "000200000000000.0000",
                EntryPlan = new("000300000000000.000", "000100000000000.", 60),
            },
            context
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
