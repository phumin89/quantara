using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class StaleResearchEvidenceChecks
{
    private static readonly string[] mutations = ["account", "policy", "quantity", "available", "mode", "late"];

    public static async Task Run()
    {
        foreach (var mutation in mutations)
        {
            var store = new FakeTeamRunStore();
            var evidence = new MutableResearchSnapshotReader();
            var time = new ManualTimeProvider();
            var worker = new FakeCodexWorker(
                (string role, IReadOnlyList<(string Role, string Output)> _, CancellationToken _) =>
                {
                    evidence.Mutation = mutation;
                    if (mutation == "late")
                    {
                        time.Advance(TimeSpan.FromMinutes(21));
                    }

                    return Task.FromResult(
                        new ResearchReport(role + " stale result", null, Usage: new ResearchUsage(12, 4, 2))
                    );
                }
            );
            using var coordinator = new TeamCoordinator(
                store,
                worker,
                evidence,
                account: new FakeCurrentAccountReader(new string('a', 64)),
                time: time
            );
            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.Initialization;
            try
            {
                var started = await coordinator.StartAutomatic("bounded evidence review", CancellationToken.None);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var run = await store.WaitFor(
                    started.Id,
                    value => value.Status is TeamRunStatus.Failed or TeamRunStatus.Completed,
                    timeout.Token
                );
                Check(
                    run.Status == TeamRunStatus.Failed && worker.Calls.Count == 1,
                    mutation + ": stale specialist cannot trigger Reviewer or publish an assessment."
                );
                Check(
                    run.Assessment is null && run.Steps[0].Output is null && run.Steps[0].Usage?.InputTokens == 12,
                    mutation + ": rejected results retain actual usage without accepting their claims."
                );
                Check(
                    run.Error is { } runError
                        && run.Steps[0].Error is { } stepError
                        && runError.Contains("worker_evidence_changed", StringComparison.Ordinal)
                        && !runError.Contains("runtime_failed", StringComparison.Ordinal)
                        && stepError.Contains("worker_evidence_changed", StringComparison.Ordinal)
                        && !stepError.Contains("runtime_failed", StringComparison.Ordinal),
                    mutation + ": stale evidence uses the specific safe error code."
                );
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None);
            }
        }

        foreach (var mutation in mutations)
        {
            await RequestedRefresh(mutation);
        }

        await RequestedRefresh(null);

        Console.WriteLine("Research lineage: account, policy, balance, mode and expired output rejection passed.");
    }

    private static async Task RequestedRefresh(string? mutation)
    {
        var store = new FakeTeamRunStore();
        var time = new ManualTimeProvider();
        var evidence = new MutableResearchSnapshotReader
        {
            RequestedMutation = mutation,
            EmptyHoldings = true,
            OnRequestedRead = () =>
            {
                if (mutation == "late")
                {
                    time.Advance(TimeSpan.FromMinutes(21));
                }
            },
        };
        var publisher = new FakeOrderProposalPublisher();
        var worker = new FakeCodexWorker(
            (string role, IReadOnlyList<(string Role, string Output)> _, CancellationToken _) =>
                Task.FromResult(
                    new ResearchReport(
                        role + " result",
                        null,
                        ResearchSymbols: role == "intraday" ? ["BTC"] : null,
                        Usage: new ResearchUsage(12, 4, 2)
                    )
                )
        );
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            evidence,
            publisher,
            account: new FakeCurrentAccountReader(new string('a', 64)),
            time: time
        );
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.Initialization;
        try
        {
            var started = await coordinator.StartAutomatic("requested market refresh", CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var run = await store.WaitFor(
                started.Id,
                value => value.Status is TeamRunStatus.Failed or TeamRunStatus.Completed,
                timeout.Token
            );
            var specialist = run.Steps.Single(step => step.Role == TeamRole.Intraday);
            Check(evidence.RequestedCount == 1, "Exactly one requested-market fetch exercises the refresh boundary.");
            Check(
                specialist.Status == TeamRunStatus.Completed
                    && specialist.Output == "intraday result"
                    && specialist.Usage?.InputTokens == 12,
                "Refresh failures preserve the already accepted specialist report and actual usage."
            );
            Check(publisher.Calls.Count == 0 && run.Assessment is null, "No proposal or assessment was published.");
            if (mutation is not null)
            {
                Check(
                    run.Status == TeamRunStatus.Failed
                        && worker.Calls.Count == 1
                        && run.Steps.Single(step => step.Role == TeamRole.Reviewer).Status == TeamRunStatus.Cancelled
                        && run.Error?.Contains("worker_evidence_changed", StringComparison.Ordinal) == true,
                    mutation + ": a changed refresh must not launch Reviewer. " + run.Error
                );
            }
            else
            {
                Check(
                    run.Status == TeamRunStatus.Completed
                        && worker.Calls.Select(call => call.Role).SequenceEqual(["intraday", "reviewer"]),
                    "New market prices with unchanged account facts reach Reviewer. " + run.Error
                );
                using var context = JsonDocument.Parse(
                    worker.Calls[1].Portfolio ?? throw new InvalidOperationException("Reviewer context missing.")
                );
                Check(
                    context
                        .RootElement.GetProperty("evidence")
                        .GetProperty("marketResearch")
                        .GetProperty("markets")[0]
                        .GetProperty("last")
                        .GetDecimal() == 101m,
                    "Reviewer receives refreshed prices, not the original snapshot."
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
