using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class StartupContextChecks
{
    public static async Task Run()
    {
        foreach (var missing in new[] { true, false })
        {
            var account = new string(missing ? 'a' : 'b', 64);
            var store = new FakeTeamRunStore();
            var worker = new FakeCodexWorker((_, _, _) => Task.FromResult("Must not run"));
            IPortfolioSnapshotReader snapshot = missing
                ? new LoopSnapshotReader { Available = false }
                : new FakePortfolioSnapshotReader();
            using var coordinator = new TeamCoordinator(
                store,
                worker,
                snapshot,
                account: new FakeCurrentAccountReader(account)
            );
            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                var started = await coordinator.Start("Verify evidence collection", CancellationToken.None);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var failed = await store.WaitFor(started.Id, run => run.Status == TeamRunStatus.Failed, timeout.Token);
                var visible = await store.LatestForAccount(account, timeout.Token);
                if (visible?.Id != failed.Id || failed.AccountKey != account || worker.Calls.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Snapshot failures must remain visible only to the starting account and never execute a mismatched-account research job."
                    );
                }
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None);
            }
        }

        Console.WriteLine(
            "Research startup: visible evidence failures, pinned account and no mismatched-account model calls passed."
        );
    }
}
