using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class TradingModeProposalChecks
{
    public static async Task Run()
    {
        foreach (var mode in new string?[] { null, "paper", "stopped", "autoLive", "unknown" })
        {
            var store = new FakeTeamRunStore();
            var publisher = new FakeOrderProposalPublisher();
            var proposal = new TradeProposal(
                "BTC",
                TradeProposalSide.Buy,
                "0.01",
                "100",
                "Fixture research",
                FakePortfolioSnapshotReader.FetchedAt
            );
            var worker = new FakeCodexWorker(
                (role, _, _) => Task.FromResult(new ResearchReport(role, role == "reviewer" ? proposal : null))
            );
            using var coordinator = new TeamCoordinator(
                store,
                worker,
                new FakePortfolioSnapshotReader { TradingMode = mode },
                publisher
            );
            await coordinator.StartAsync(CancellationToken.None);
            await coordinator.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                var started = await coordinator.Start("Research only", CancellationToken.None);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var completed = await store.WaitFor(
                    started.Id,
                    run => run.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                    timeout.Token
                );
                Check(
                    completed.Status == TeamRunStatus.Completed
                        && worker.Calls.Count == 3
                        && publisher.Calls.Count == 0,
                    "Missing or non-manual mode must preserve completed research without publishing."
                );
                Check(
                    completed.Decision == LearningDecision.NoProposal
                        && completed.Steps[^1].Output?.Contains("ข้อเสนอถูกข้าม", StringComparison.Ordinal) == true,
                    "Ignored proposals must be visible and recorded as research only."
                );
            }
            finally
            {
                await coordinator.StopAsync(CancellationToken.None);
            }
        }

        var snapshot = JsonSerializer.Serialize(
            new
            {
                positions = Array.Empty<object>(),
                summary = new
                {
                    totalAssets = (decimal?)null,
                    availableThb = 700m,
                    holdingCount = 0,
                },
                updatedAt = DateTimeOffset.UtcNow,
                priceUpdatedAt = (DateTimeOffset?)null,
                historyUpdatedAt = (DateTimeOffset?)null,
                historyCoverage = "Fixture",
                syncing = false,
                error = (string?)null,
            }
        );
        var account = new string('a', 64);
        foreach (var mode in new[] { "paper", "stopped", "autoLive", "manualReview", "unknown" })
        {
            using var handler = new SnapshotHttpHandler(
                snapshot,
                trading: JsonSerializer.Serialize(
                    new
                    {
                        accountKey = account,
                        mode,
                        ownerToken = "must-not-enter-research",
                    }
                )
            );
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://portfolio.invalid/") };
            var result = await new PortfolioSnapshotReader(http).Read(CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            var actual = document.RootElement.GetProperty("tradingMode");
            Check(
                handler.TradingReads == 1
                    && (mode == "unknown" ? actual.ValueKind == JsonValueKind.Null : actual.GetString() == mode),
                "Research mode must come from a validated read-only account-scoped response."
            );
            Check(
                !result.Contains("must-not-enter-research", StringComparison.Ordinal),
                "Trading-control fields are excluded from research."
            );
        }

        using var mismatch = new SnapshotHttpHandler(
            snapshot,
            trading: JsonSerializer.Serialize(new { accountKey = new string('b', 64), mode = "manualReview" })
        );
        using var mismatchHttp = new HttpClient(mismatch) { BaseAddress = new Uri("http://portfolio.invalid/") };
        try
        {
            await new PortfolioSnapshotReader(mismatchHttp).Read(CancellationToken.None);
            throw new InvalidOperationException("Mismatched trading-mode account was accepted.");
        }
        catch (InvalidOperationException error)
            when (error.Message.StartsWith("Trading mode evidence belongs", StringComparison.Ordinal)) { }

        Console.WriteLine(
            "Research trading-mode boundaries: non-manual proposals ignored, reports retained, account/mode whitelist and no control authority passed."
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
