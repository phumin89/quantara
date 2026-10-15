using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class RequestedEvidenceChecks
{
    public static async Task Run()
    {
        await Verify(false, "KUB", true);
        await Verify(true, "KUB", false);
        await Verify(false, "UNKNOWN", false);
    }

    private static async Task Verify(bool changeAccount, string selected, bool succeeds)
    {
        var store = new FakeTeamRunStore();
        var evidence = new RequestedSnapshotReader { ChangeAccount = changeAccount };
        var publisher = new FakeOrderProposalPublisher();
        var worker = new FakeCodexWorker(
            (role, _, _) =>
            {
                if (role == "ceo")
                {
                    return Task.FromResult(
                        new ResearchReport("Select one researched candidate", null, null, [selected])
                    );
                }

                return Task.FromResult(
                    new ResearchReport(
                        "Fresh evidence evaluated",
                        role == "reviewer"
                            ? new TradeProposal(
                                "KUB",
                                TradeProposalSide.Buy,
                                "100",
                                "18",
                                "Supported resting entry",
                                evidence.FreshAt,
                                new TradeEntryPlan("19", "17", 60)
                            )
                            : null
                    )
                );
            }
        );
        using var coordinator = new TeamCoordinator(store, worker, evidence, publisher);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.Initialization;
        try
        {
            var started = await coordinator.Start("Research resting entries", CancellationToken.None);
            var completed = await store.WaitFor(
                started.Id,
                run => run.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                CancellationToken.None
            );
            Check(
                (completed.Status == TeamRunStatus.Completed) == succeeds,
                "Requested evidence completion/account boundary."
            );
            Check(publisher.Calls.Count == (succeeds ? 1 : 0), "Only a supported same-account proposal is published.");
            Check(
                evidence.Requested.Count == (selected == "UNKNOWN" ? 0 : 1),
                "Unknown symbols must be rejected before fetching."
            );
            if (succeeds)
            {
                foreach (var call in worker.Calls.Skip(1))
                {
                    using var document = JsonDocument.Parse(
                        call.Portfolio ?? throw new InvalidOperationException("Missing context.")
                    );
                    Check(
                        document
                            .RootElement.GetProperty("evidence")
                            .GetProperty("marketResearch")
                            .GetProperty("markets")[0]
                            .GetProperty("symbol")
                            .GetString() == "KUB_THB",
                        "Analyst and Reviewer must receive requested fresh evidence."
                    );
                }

                Check(
                    worker.Calls.Count == 3 && publisher.Calls[0].Proposal.LimitPrice == "18",
                    "Complete ordered handoff preserves the exact resting price."
                );
            }
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
