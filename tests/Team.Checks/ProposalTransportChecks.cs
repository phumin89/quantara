using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ProposalTransportChecks
{
    public static async Task Run()
    {
        var runId = Guid.NewGuid();
        var handler = new PublisherHttpHandler(runId);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        var publisher = new OrderProposalPublisher(http);
        var proposal = new TradeProposal("BTC", TradeProposalSide.Buy, "1", "100", "review", DateTimeOffset.UtcNow);
        await publisher.Publish(runId, new string('a', 64), proposal, CancellationToken.None);
        Check(handler.Calls == 1 && handler.Request?.Method == HttpMethod.Post, "proposal POST only");
        Check(handler.Request?.RequestUri?.AbsolutePath == "/api/crypto/orders", "proposal route");
        Check(
            handler.Request?.Headers.GetValues("X-Quantara-Order-Action").Single() == "review",
            "proposal review header"
        );
        Check(
            handler.Request?.RequestUri is { } uri
                && !uri.AbsolutePath.Contains("approve", StringComparison.OrdinalIgnoreCase),
            "proposal never approve"
        );

        var staleHandler = new PublisherHttpHandler();
        using var staleHttp = new HttpClient(staleHandler) { BaseAddress = new Uri("http://portfolio.invalid/") };
        var stalePublisher = new OrderProposalPublisher(staleHttp);
        try
        {
            await stalePublisher.Publish(
                runId,
                new string('a', 64),
                proposal with
                {
                    EvidenceAt = DateTimeOffset.UtcNow.AddMinutes(-11),
                },
                CancellationToken.None
            );
            throw new InvalidOperationException("stale proposal accepted");
        }
        catch (InvalidOperationException) { }

        Check(staleHandler.Calls == 0, "stale proposal rejected before network");

        var output = new string('x', 5001);
        var json = JsonSerializer.Serialize(
            new TeamRun(
                runId,
                "x",
                TeamRunStatus.Completed,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, output, null)],
                null,
                new string('a', 64)
            )
        );
        using var document = JsonDocument.Parse(json);
        Check(
            document.RootElement.GetProperty("Steps")[0].GetProperty("Role").GetInt32() == (int)TeamRole.Reviewer,
            "SQL payload role numeric"
        );
        Check(
            document.RootElement.GetProperty("Steps")[0].GetProperty("Status").GetInt32()
                == (int)TeamRunStatus.Completed,
            "SQL payload status numeric"
        );
        Check(
            document.RootElement.GetProperty("Steps")[0].GetProperty("Output").GetString()?.Length == 5001,
            "long reviewer output retained"
        );
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Check failed: {name}");
        }
    }
}
