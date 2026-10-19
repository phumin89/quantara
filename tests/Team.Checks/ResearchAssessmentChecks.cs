using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchAssessmentChecks
{
    public static async Task Run()
    {
        var account = new string('a', 64);
        var created = DateTimeOffset.UtcNow;
        var run = new TeamRun(Guid.NewGuid(), "assessment", TeamRunStatus.Running, created, created, [], null, account);
        var evidence = """
            {"policyHash":"policy-1","positions":[{"symbol":"ETH","quantity":1}],"walletEvidence":{"balances":[{"symbol":"BTC","quantity":1},{"symbol":"THB","quantity":5000}]},"marketResearch":{"available":true,"scan":{"symbols":["BTC_THB","ETH_THB"]}}}
            """;
        var news = "{\"events\":[{\"eventId\":\"n1\",\"revision\":2}]}";
        using var evidenceDocument = JsonDocument.Parse(evidence);
        using var newsDocument = JsonDocument.Parse(news);
        var context = JsonSerializer.Serialize(
            new
            {
                evidence = evidenceDocument.RootElement,
                news = newsDocument.RootElement,
                assessmentContext = ResearchEvidence.Context(
                    run,
                    evidenceDocument.RootElement,
                    newsDocument.RootElement
                ),
            }
        );
        using var document = JsonDocument.Parse(context);
        var asOf = document.RootElement.GetProperty("assessmentContext").GetProperty("asOf").GetDateTimeOffset();
        var valid = new ResearchAssessment(
            "research-assessment-v1",
            account,
            run.Id,
            "policy-1",
            asOf,
            created.AddHours(1),
            ["portfolio:" + run.Id.ToString("D"), "market:" + run.Id.ToString("D"), "news:n1@2"],
            [
                new PositionAssessment(
                    "BTC",
                    "Keep",
                    "No frozen entry thesis",
                    "Review current evidence",
                    created.AddMinutes(5),
                    created.AddHours(1),
                    ["entryThesis"]
                ),
            ],
            [
                new ResearchOpportunity(
                    "ETH",
                    "Setup needs confirmation",
                    "Fresh spread and candle evidence",
                    created.AddMinutes(5)
                ),
            ]
        );
        ResearchAssessmentValidator.Validate(valid, run, account, context);

        CheckRejected(valid with { AccountKey = new string('b', 64) }, run, account, context, "wrong account");
        CheckRejected(valid with { RunId = Guid.NewGuid() }, run, account, context, "wrong run");
        CheckRejected(valid with { PolicyHash = "wrong" }, run, account, context, "wrong policy");
        CheckRejected(valid with { AsOf = asOf.AddSeconds(-1) }, run, account, context, "altered evidence cutoff");
        CheckRejected(valid with { EvidenceIds = ["news:n1@99"] }, run, account, context, "invented news revision");
        CheckRejected(
            valid with
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            run,
            account,
            context,
            "expired assessment"
        );
        CheckRejected(
            valid with
            {
                Opportunities = [new ResearchOpportunity("DOGE", "x", "y", created.AddMinutes(5))],
            },
            run,
            account,
            context,
            "opportunity outside scan"
        );
        ResearchAssessmentValidator.Validate(
            valid with
            {
                Positions = [valid.Positions[0] with { MissingFields = [] }],
            },
            run,
            account,
            context
        );
        await RoundTrip();
    }

    private static async Task RoundTrip()
    {
        var account = new string('a', 64);
        var store = new FakeTeamRunStore();
        FakeCodexWorker? worker = null;
        worker = new FakeCodexWorker(
            (role, _, _) =>
            {
                if (role != "reviewer")
                {
                    return Task.FromResult(new ResearchReport(role + " report", null));
                }

                using var context = JsonDocument.Parse(
                    worker?.Calls[^1].Portfolio ?? throw new InvalidOperationException("Missing role context.")
                );
                var template = context.RootElement.GetProperty("assessmentContext");
                var asOf = template.GetProperty("asOf").GetDateTimeOffset();
                var assessment = new ResearchAssessment(
                    "research-assessment-v1",
                    account,
                    template.GetProperty("runId").GetGuid(),
                    null,
                    asOf,
                    asOf.AddHours(1),
                    template
                        .GetProperty("evidenceIds")
                        .EnumerateArray()
                        .Select(item => item.GetString() ?? "")
                        .ToArray(),
                    [
                        new PositionAssessment(
                            "BTC",
                            "Review",
                            "Legacy entry thesis unknown",
                            "Inspect supplied evidence",
                            asOf.AddMinutes(15),
                            asOf.AddHours(1),
                            ["entryThesis"]
                        ),
                    ],
                    []
                );
                return Task.FromResult(new ResearchReport("Reviewed supplied news", null, null, null, assessment));
            }
        );
        using var coordinator = new TeamCoordinator(
            store,
            worker,
            new FakePortfolioSnapshotReader { TradingMode = "stopped" },
            account: new FakeCurrentAccountReader(account),
            news: new FakeNewsReader("{\"events\":[{\"eventId\":\"news-one\",\"revision\":2}]}")
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await coordinator.StartAsync(timeout.Token);
        await coordinator.Initialization;
        try
        {
            var run = await coordinator.Start("Read-only news assessment", timeout.Token);
            var completed = await store.WaitFor(
                run.Id,
                item => item.Status is TeamRunStatus.Completed or TeamRunStatus.Failed,
                timeout.Token
            );
            if (
                completed.Status != TeamRunStatus.Completed
                || completed.Assessment?.RunId != run.Id
                || completed.Assessment.AccountKey != account
                || !completed.Assessment.EvidenceIds.Contains("news:news-one@2", StringComparer.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "Read-only assessment did not round-trip through the coordinator: " + completed.Error
                );
            }
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private static void CheckRejected(
        ResearchAssessment assessment,
        TeamRun run,
        string account,
        string context,
        string reason
    )
    {
        try
        {
            ResearchAssessmentValidator.Validate(assessment, run, account, context);
            throw new InvalidOperationException("Accepted invalid assessment: " + reason);
        }
        catch (CodexWorkerException error) when (error.Code == "worker_response_invalid") { }
    }
}
