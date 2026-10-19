using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchContextBudgetChecks
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task Run()
    {
        await RefreshedEvidence(48000, true);
        await RefreshedEvidence(36000, false);
        await RefreshedEvidence(60000, false, false);
        Console.WriteLine(
            "Research context: refreshed evidence and exact role handoffs fit context and wire limits without losing account facts."
        );
    }

    private static async Task RefreshedEvidence(int evidenceCharacters, bool heavyHandoff, bool fits = true)
    {
        var account = new string('a', 64);
        var store = new FakeTeamRunStore();
        var now = DateTimeOffset.UtcNow;
        var priorReview = new string('ท', 16000);
        for (var index = 0; index < 24; index++)
        {
            var at = now.AddMinutes(-65 - index);
            await store.Save(
                new TeamRun(
                    Guid.NewGuid(),
                    "retained research",
                    TeamRunStatus.Completed,
                    at,
                    at,
                    [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, priorReview, null)],
                    null,
                    account,
                    new LearningSnapshot(at, [new LearningQuote("BTC_THB", 100m)]),
                    null,
                    new LearningLesson(new string('l', 500), new string('e', 500), new string('t', 500))
                ),
                CancellationToken.None
            );
        }

        var evidence = new RequestedSnapshotReader { RefreshedEvidenceCharacters = evidenceCharacters };
        var ceo = heavyHandoff ? new string('ก', 12000) : "Inspect KUB";
        var analyst = heavyHandoff ? new string('"', 16000) : "Fresh evidence inspected";
        var worker = new FakeCodexWorker(
            (role, _, _) =>
                Task.FromResult(
                    role == "ceo"
                        ? new ResearchReport(ceo, null, null, ["KUB"])
                        : new ResearchReport(role == "analyst" ? analyst : "Review complete", null)
                )
        );
        using var coordinator = new TeamCoordinator(store, worker, evidence);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await coordinator.StartAsync(timeout.Token);
        await coordinator.Initialization;
        try
        {
            var started = await coordinator.Start("Bounded research", timeout.Token);
            var completed = await store.WaitFor(
                started.Id,
                run => run.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                timeout.Token
            );
            if (!fits)
            {
                Check(
                    completed.Status == TeamRunStatus.Failed
                        && completed.Error?.Contains("[worker_input_too_large]", StringComparison.Ordinal) == true
                        && worker.Calls.Count == 1,
                    "Irreducible evidence fails visibly before another model call, without truncating facts."
                );
                return;
            }

            Check(
                completed.Status == TeamRunStatus.Completed,
                "Fresh requested evidence must complete all three roles: " + completed.Error
            );
            Check(worker.Calls.Count == 3, "Each role executes exactly once.");
            foreach (var call in worker.Calls)
            {
                var context = call.Portfolio ?? throw new InvalidOperationException("Missing research context.");
                var wire = JsonSerializer.Serialize(
                    new
                    {
                        role = call.Role,
                        objective = call.Objective,
                        previous = call.Previous.Select(item => new { role = item.Role, output = item.Output }),
                        portfolio = context,
                    },
                    ResearchContextBudgetChecks.jsonOptions
                );
                Check(
                    context.Length <= 60000 && wire.Length <= 100000,
                    "Both context and serialized request must fit unchanged worker limits."
                );
                using var document = JsonDocument.Parse(context);
                var learning = document.RootElement.GetProperty("dailyLearning");
                Check(
                    learning.GetProperty("sourceRoundCount").GetInt32() == 24,
                    "Compaction preserves all account-scoped learning totals."
                );
                if (call.Role == "ceo")
                {
                    continue;
                }

                Check(
                    JsonNode.DeepEquals(
                        JsonNode.Parse(document.RootElement.GetProperty("evidence").GetRawText()),
                        JsonNode.Parse(
                            evidence.RefreshedSnapshot
                                ?? throw new InvalidOperationException("Missing refreshed snapshot.")
                        )
                    ),
                    "Full fresh wallet, scan and market evidence must remain unchanged."
                );
                Check(
                    document.RootElement.GetProperty("priorRunId").ValueKind == JsonValueKind.String,
                    "Retained prior review remains traceable."
                );
                Check(
                    document.RootElement.GetProperty("priorReviewOmitted").GetBoolean() == heavyHandoff,
                    "Whole-review omission is disclosed only when required."
                );
            }

            Check(
                worker.Calls[2].Previous[0].Output == ceo && worker.Calls[2].Previous[1].Output == analyst,
                "Role outputs are preserved exactly, including Thai and escaped quotation marks."
            );
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
