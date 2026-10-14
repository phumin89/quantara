using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Team.Checks;

public static class DailyLearningChecks
{
    private static readonly string[] fixtureSymbols = ["BTC_THB", "ETH_THB"];

    private static readonly bool[] fixtureUsable = [true, false];

    public static async Task Run()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new string('a', 64);
        var current = new LearningSnapshot(now, [new LearningQuote("BTC_THB", 110m)]);
        var store = new FakeTeamRunStore();
        for (var days = 0; days < 9; days++)
        {
            var observed = now.AddDays(-days).AddMinutes(-10);
            var run = new TeamRun(
                Guid.NewGuid(),
                "Review entry and Wait",
                TeamRunStatus.Completed,
                observed,
                observed,
                [
                    new TeamStep(
                        TeamRole.Reviewer,
                        TeamRunStatus.Completed,
                        "Learning: entry hypothesis needs forward costs",
                        null
                    ),
                ],
                null,
                account,
                new LearningSnapshot(observed, [new LearningQuote("BTC_THB", 100m), new LearningQuote("ETH_THB", 50m)])
            );
            await store.Save(run, CancellationToken.None);
            await store.Save(
                run with
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = observed.AddMinutes(-1),
                },
                CancellationToken.None
            );
            await store.Save(
                run with
                {
                    Id = Guid.NewGuid(),
                    AccountKey = new string('b', 64),
                },
                CancellationToken.None
            );
            await store.Save(
                run with
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = observed.AddMinutes(1),
                    Status = TeamRunStatus.Failed,
                },
                CancellationToken.None
            );
        }

        var history = await store.LearningHistory(account, now, CancellationToken.None);
        Check(
            history.Count == 7
                && history.All(run => run.AccountKey == account && run.Status == TeamRunStatus.Completed),
            "isolated seven-day completed history"
        );
        using var review = JsonDocument.Parse(DailyLearning.Review(account, now, current, history));
        var first = review.RootElement.GetProperty("days")[0];
        Check(
            first.GetProperty("outcomes")[0].GetProperty("grossReferenceMovePercent").GetDecimal() == 10m,
            "forward reference move"
        );
        Check(
            first.GetProperty("comparedMarketCount").GetInt32() == 1
                && first.GetProperty("sourceMarketCount").GetInt32() == 2,
            "missing later quote never becomes zero"
        );
        Check(
            review.RootElement.GetProperty("realizedNetProfitThb").ValueKind == JsonValueKind.Null,
            "reference gain is not realized profit"
        );
        using var bounded = JsonDocument.Parse(DailyLearning.Review(account, now, current, history, 1000));
        Check(bounded.RootElement.GetProperty("omittedDayCount").GetInt32() > 0, "budget omissions explicit");
        using var early = JsonDocument.Parse(
            DailyLearning.Review(account, now, new LearningSnapshot(now.AddMinutes(-9), current.Markets), history)
        );
        Check(
            early.RootElement.GetProperty("days")[0].GetProperty("outcomes").GetArrayLength() == 0,
            "no premature outcome"
        );
        var evidence = JsonSerializer.Serialize(
            new
            {
                marketResearch = new
                {
                    available = true,
                    fetchedAt = now,
                    scan = new
                    {
                        symbols = DailyLearningChecks.fixtureSymbols,
                        last = new decimal?[] { 110m, null },
                        usable = DailyLearningChecks.fixtureUsable,
                    },
                },
            }
        );
        Check(DailyLearning.Capture(evidence)?.Markets.Count == 1, "capture excludes unusable quote");
        Check(DailyLearning.Capture("{}") is null, "legacy evidence has no invented memory");
        var snapshot = await new FakePortfolioSnapshotReader().Read(CancellationToken.None);
        Check(DailyLearning.Capture(snapshot)?.Markets.Count == 1, "real context shape captures learning");
        using var overflow = JsonDocument.Parse(
            DailyLearning.Review(
                account,
                now,
                new LearningSnapshot(now, [new LearningQuote("BTC_THB", decimal.MaxValue)]),
                [
                    history[0] with
                    {
                        Learning = new LearningSnapshot(
                            now.AddMinutes(-10),
                            [new LearningQuote("BTC_THB", 0.0000000000000000000000000001m)]
                        ),
                    },
                ]
            )
        );
        Check(
            overflow.RootElement.GetProperty("days")[0].GetProperty("outcomes").GetArrayLength() == 0,
            "overflow remains unknown"
        );
        Console.WriteLine(
            "Daily learning: account isolation, Bangkok dates, reference outcomes, unknown profit, bounded memory and legacy capture passed."
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
