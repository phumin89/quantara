using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public static class DailyLearningChecks
{
    public static async Task Run()
    {
        var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        var observed = now.AddHours(-2);
        var first = new TeamRun(
            Guid.NewGuid(),
            "first decision",
            TeamRunStatus.Completed,
            observed,
            observed,
            [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, new string('x', 4000), null)],
            null,
            account,
            new LearningSnapshot(
                observed,
                [new LearningQuote("BTC_THB", 100m), new LearningQuote("ETH_THB", 50m)],
                observed.AddHours(1)
            ),
            null,
            new LearningLesson("keep full lesson", "fixed reference", "compare baseline"),
            LearningDecision.NoProposal
        );
        var second = first with
        {
            Id = Guid.NewGuid(),
            CreatedAt = observed.AddMinutes(10),
            Decision = LearningDecision.BuyProposal,
        };
        var store = new FakeTeamRunStore();
        await store.Save(first, CancellationToken.None);
        await store.Save(second, CancellationToken.None);
        await store.Save(first with { Id = Guid.NewGuid(), AccountKey = new string('b', 64) }, CancellationToken.None);
        await store.Save(first with { Id = Guid.NewGuid(), Status = TeamRunStatus.Failed }, CancellationToken.None);
        await store.Save(first with { Id = Guid.NewGuid(), CreatedAt = now.AddMinutes(1) }, CancellationToken.None);
        await store.Save(first with { Id = Guid.NewGuid(), CreatedAt = now.AddDays(-8) }, CancellationToken.None);
        var history = await store.LearningHistory(account, now, CancellationToken.None);
        Check(history.Count == 2, "all same-day decisions retained; failed, future, old and other account excluded");
        var endpoint = first with
        {
            Id = Guid.NewGuid(),
            CreatedAt = observed.AddHours(1),
            Learning = new LearningSnapshot(observed.AddHours(1).AddMinutes(2), [new LearningQuote("BTC_THB", 110m)]),
        };
        var current = first with
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            Learning = new LearningSnapshot(now, [new LearningQuote("BTC_THB", 180m)]),
        };
        var frozen =
            DailyLearning.Evaluate(first, [endpoint], current)
            ?? throw new InvalidOperationException("endpoint missing");
        Check(
            frozen.SourceRunId == endpoint.Id && frozen.Markets[0].Price == 110m,
            "first mature observation selected at fixed one-hour deadline"
        );
        var evaluated = first with { LearningEvaluation = frozen };
        Check(
            DailyLearning.Evaluate(evaluated, [], current)?.Markets[0].Price == 110m,
            "old result never repriced by later market"
        );
        Check(DailyLearning.Evaluate(first, [], current) is null, "late endpoint remains unknown");
        var early = current with
        {
            Learning = new LearningSnapshot(
                observed.AddMinutes(59),
                (current.Learning ?? throw new InvalidOperationException("current missing")).Markets
            ),
        };
        Check(DailyLearning.Evaluate(first, [], early) is null, "before horizon cannot evaluate");
        Check(
            DailyLearning.Evaluate(first, [endpoint with { AccountKey = new string('b', 64) }], current) is null,
            "endpoint account isolation"
        );
        using var review = JsonDocument.Parse(
            DailyLearning.Review(account, now, current.Learning, [evaluated, second])
        );
        var day = review.RootElement.GetProperty("days")[0];
        Check(
            day.GetProperty("roundCount").GetInt32() == 2
                && day.GetProperty("buyProposalCount").GetInt32() == 1
                && day.GetProperty("noProposalCount").GetInt32() == 1,
            "every decision contributes to daily totals"
        );
        var round = day.GetProperty("rounds")
            .EnumerateArray()
            .Single(item => item.GetProperty("runId").GetGuid() == first.Id);
        Check(
            round.GetProperty("outcomes")[0].GetProperty("grossReferenceMovePercent").GetDecimal() == 10m,
            "frozen forward movement"
        );
        Check(
            round.GetProperty("comparedMarketCount").GetInt32() == 1
                && round.GetProperty("sourceMarketCount").GetInt32() == 2,
            "missing endpoint price never becomes zero"
        );
        Check(
            round.GetProperty("lesson").GetProperty("Lesson").GetString() == "keep full lesson",
            "lesson survives long report text"
        );
        Check(
            review.RootElement.GetProperty("dailyNetProfitTargetThb").GetDecimal() == 500m
                && review.RootElement.GetProperty("realizedNetProfitThb").ValueKind == JsonValueKind.Null,
            "500 net target is distinct from hypothetical gains"
        );
        using var bounded = JsonDocument.Parse(
            DailyLearning.Review(account, now, current.Learning, [evaluated, second], 1500)
        );
        Check(
            bounded.RootElement.GetProperty("days")[0].GetProperty("roundCount").GetInt32() == 2,
            "bounded context still counts all rounds"
        );
        var legacy =
            JsonSerializer.Deserialize<TeamRun>(
                JsonSerializer.Serialize(
                    first with
                    {
                        Learning = new LearningSnapshot(
                            observed,
                            (first.Learning ?? throw new InvalidOperationException("first missing")).Markets
                        ),
                        Lesson = null,
                        Decision = null,
                    }
                )
            ) ?? throw new InvalidOperationException("legacy missing");
        Check(
            DailyLearning.Evaluate(legacy, [endpoint], current) is null && legacy.Decision is null,
            "legacy deadline/decision not invented"
        );
        var snapshot = await new FakePortfolioSnapshotReader().Read(CancellationToken.None);
        var captured = DailyLearning.Capture(snapshot) ?? throw new InvalidOperationException("capture failed");
        Check(captured.EvaluationAt == captured.ObservedAt.AddHours(1), "evaluation deadline pinned before research");
        Check(DailyLearning.Capture("{}") is null, "missing learning remains unknown");
        Console.WriteLine(
            "Daily learning: all rounds, frozen horizons, complete typed lessons, account isolation, unknowns and 500 THB net target passed."
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
