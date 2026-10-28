using System.Data.SqlTypes;
using System.Globalization;
using Quantara.Team.Domain;

namespace Team.Checks;

public static class FakeTeamRunStoreChecks
{
    private static readonly DateTimeOffset observedAt = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly string accountKey = new('a', 64);

    private static readonly Guid sqlLowerId = new("01000000-0000-0000-0000-000000000000");

    private static readonly Guid sqlHigherId = new("00000000-0000-0000-0000-000000000001");

    public static async Task Run()
    {
        await SqlGuidOrdering();
        await ReviewedOutput();
        await ResearchCap();
        await ResearchBoundaries();
        Console.WriteLine(
            "Fake team store: SQL GUID ordering, ASCII-space review filtering and bounded research queries."
        );
    }

    private static async Task SqlGuidOrdering()
    {
        Check(
            sqlLowerId.CompareTo(sqlHigherId) > 0 && new SqlGuid(sqlLowerId).CompareTo(new SqlGuid(sqlHigherId)) < 0,
            "Fixture IDs must distinguish Guid ordering from SQL uniqueidentifier ordering."
        );
        var store = new FakeTeamRunStore();
        var createdAt = observedAt.AddHours(-1);
        await store.Save(CreateRun(sqlLowerId, createdAt), CancellationToken.None);
        await store.Save(CreateRun(sqlHigherId, createdAt), CancellationToken.None);

        Check(
            (await store.Latest(CancellationToken.None))?.Id == sqlHigherId,
            "Latest uses descending SQL GUID order when creation times tie."
        );
        Check(
            (await store.LatestForAccount(accountKey, CancellationToken.None))?.Id == sqlHigherId,
            "LatestForAccount uses descending SQL GUID order when creation times tie."
        );
        Check(
            (await store.LatestReviewed(accountKey, observedAt, CancellationToken.None))?.Id == sqlHigherId,
            "LatestReviewed uses descending SQL GUID order when creation times tie."
        );
        CheckIds(
            await store.LearningHistory(accountKey, observedAt, CancellationToken.None),
            [sqlHigherId, sqlLowerId],
            "LearningHistory uses descending SQL GUID order."
        );
        CheckIds(
            await store.RecentResearch(accountKey, observedAt, CancellationToken.None),
            [sqlHigherId, sqlLowerId],
            "RecentResearch uses descending SQL GUID order."
        );
        CheckIds(
            await store.ForDay(
                DateOnly.FromDateTime(createdAt.ToOffset(TimeSpan.FromHours(7)).DateTime),
                CancellationToken.None
            ),
            [sqlLowerId, sqlHigherId],
            "ForDay uses ascending SQL GUID order."
        );
    }

    private static async Task ReviewedOutput()
    {
        (string? Output, bool Reviewed)[] cases =
        [
            (null, false),
            ("", false),
            (" ", false),
            ("   ", false),
            ("\t", true),
            ("\n", true),
            (" \t ", true),
            (" \r\n ", true),
        ];
        foreach (var (output, reviewed) in cases)
        {
            var store = new FakeTeamRunStore();
            var older = CreateRun(sqlLowerId, observedAt.AddHours(-2));
            var newer = CreateRun(sqlHigherId, observedAt.AddHours(-1), output);
            await store.Save(older, CancellationToken.None);
            await store.Save(newer, CancellationToken.None);
            var selected = await store.LatestReviewed(accountKey, observedAt, CancellationToken.None);
            Check(
                selected?.Id == (reviewed ? newer.Id : older.Id),
                "LatestReviewed matches SQL ASCII-space trimming without rejecting tabs or newlines."
            );
        }
    }

    private static async Task ResearchCap()
    {
        var store = new FakeTeamRunStore();
        var createdAt = observedAt.AddMinutes(-1);
        for (var index = 1; index <= 513; index++)
        {
            await store.Save(CreateRun(SequenceId(index), createdAt), CancellationToken.None);
        }

        var history = await store.RecentResearch(accountKey, observedAt, CancellationToken.None);
        CheckIds(
            history,
            Enumerable.Range(2, 512).Reverse().Select(SequenceId).ToArray(),
            "RecentResearch applies descending SQL GUID order before retaining exactly 512 equal-time rows."
        );
        Check(
            history.All(run => run.Id != SequenceId(1)),
            "The lowest SQL GUID is outside the 512-row cap even when inserted first."
        );
    }

    private static async Task ResearchBoundaries()
    {
        var store = new FakeTeamRunStore();
        var start = observedAt.AddDays(-7).AddMinutes(-1);
        await store.Save(CreateRun(SequenceId(1), start), CancellationToken.None);
        await store.Save(CreateRun(SequenceId(2), observedAt.AddMinutes(-1)), CancellationToken.None);
        await store.Save(CreateRun(SequenceId(3), observedAt), CancellationToken.None);
        await store.Save(CreateRun(SequenceId(4), start.AddTicks(-1)), CancellationToken.None);
        await store.Save(CreateRun(SequenceId(5), observedAt.AddTicks(1)), CancellationToken.None);
        await store.Save(
            CreateRun(SequenceId(6), observedAt) with
            {
                AccountKey = new string('b', 64),
            },
            CancellationToken.None
        );
        CheckIds(
            await store.RecentResearch(accountKey, observedAt, CancellationToken.None),
            [SequenceId(3), SequenceId(2), SequenceId(1)],
            "RecentResearch includes both exact window endpoints and excludes foreign, older and future rows."
        );

        foreach (var invalid in new[] { "", " ", new string('a', 63), new string('a', 65), new string('g', 64) })
        {
            try
            {
                await store.RecentResearch(invalid, observedAt, CancellationToken.None);
            }
            catch (ArgumentException)
            {
                continue;
            }

            throw new InvalidOperationException("RecentResearch accepted an invalid account key.");
        }

        await store.RecentResearch(new string('A', 64), observedAt, CancellationToken.None);
    }

    private static TeamRun CreateRun(Guid id, DateTimeOffset createdAt, string? output = "Retained review") =>
        new(
            id,
            "Synthetic fake-store contract",
            TeamRunStatus.Completed,
            createdAt,
            createdAt,
            [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, output, null)],
            null,
            accountKey,
            new LearningSnapshot(createdAt, [])
        );

    private static Guid SequenceId(int value) =>
        new("00000000-0000-0000-0000-" + value.ToString("X12", CultureInfo.InvariantCulture));

    private static void CheckIds(IReadOnlyList<TeamRun> runs, Guid[] expected, string message) =>
        Check(runs.Select(run => run.Id).SequenceEqual(expected), message);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
