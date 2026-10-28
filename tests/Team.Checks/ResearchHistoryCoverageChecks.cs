using System.Text.Json;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class ResearchHistoryCoverageChecks
{
    private const string originalNews = """{"events":[{"eventId":"A","revision":1},{"eventId":"B","revision":1}]}""";

    private const string retainedNews = """{"events":[{"eventId":"B","revision":1}]}""";

    private const string revisedNews = """{"events":[{"eventId":"B","revision":2}]}""";

    public static async Task Run()
    {
        await FakeTeamRunStoreChecks.Run();
        await RetainsActiveNewsCoverage();
        await RetainsCoverageThroughBusyHistory();
        await IsolatesAccountsAndTime();
        Console.WriteLine("Research history: active news coverage, busy history and account/time isolation passed.");
    }

    private static async Task RetainsActiveNewsCoverage()
    {
        var now = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        TimeSpan[] ages =
        [
            TimeSpan.FromHours(49),
            TimeSpan.FromDays(5),
            TimeSpan.FromDays(7).Add(TimeSpan.FromSeconds(30)),
        ];
        foreach (var age in ages)
        {
            var store = new FakeTeamRunStore();
            await store.Save(ResearchRun(account, now - age, TeamRole.News), CancellationToken.None);
            var history = await store.RecentResearch(account, now, CancellationToken.None);
            Check(
                !HasNewsCandidate(account, retainedNews, history, now),
                "An unchanged active revision remains covered across its news lifetime and publication clock tolerance."
            );
            Check(
                HasNewsCandidate(account, revisedNews, history, now),
                "A new revision remains eligible while an older revision stays covered."
            );
        }
    }

    private static async Task RetainsCoverageThroughBusyHistory()
    {
        var now = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        var store = new FakeTeamRunStore();
        await store.Save(ResearchRun(account, now.AddDays(-5), TeamRole.News), CancellationToken.None);
        for (var index = 1; index <= 193; index++)
        {
            await store.Save(
                ResearchRun(account, now.AddMinutes(-30 * index), TeamRole.Intraday),
                CancellationToken.None
            );
        }

        var history = await store.RecentResearch(account, now, CancellationToken.None);
        Check(
            !HasNewsCandidate(account, retainedNews, history, now),
            "Newer ordinary research cannot evict coverage of a still-active news revision."
        );
    }

    private static async Task IsolatesAccountsAndTime()
    {
        var now = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        var account = new string('a', 64);
        var store = new FakeTeamRunStore();
        await store.Save(ResearchRun(new string('b', 64), now.AddDays(-1), TeamRole.News), CancellationToken.None);
        await store.Save(ResearchRun(account, now.AddDays(-8), TeamRole.News), CancellationToken.None);
        await store.Save(ResearchRun(account, now.AddMinutes(1), TeamRole.News), CancellationToken.None);
        var history = await store.RecentResearch(account, now, CancellationToken.None);
        Check(history.Count == 0, "Recent research excludes foreign-account, expired-history and future runs.");
        Check(
            HasNewsCandidate(account, retainedNews, history, now),
            "Foreign-account or out-of-window coverage cannot suppress the current account's news."
        );
    }

    private static TeamRun ResearchRun(string account, DateTimeOffset createdAt, TeamRole specialist)
    {
        using var evidence = JsonDocument.Parse($$"""{"accountKey":"{{account}}","positions":[]}""");
        var task = TeamResearchRouter
            .Candidates(evidence.RootElement, originalNews, [], createdAt, false)
            .Single(candidate => candidate.Specialist == specialist);
        return new TeamRun(
            Guid.NewGuid(),
            "fixture",
            TeamRunStatus.Completed,
            createdAt,
            createdAt,
            [],
            null,
            account,
            Task: task
        );
    }

    private static bool HasNewsCandidate(
        string account,
        string news,
        IReadOnlyList<TeamRun> history,
        DateTimeOffset now
    )
    {
        using var evidence = JsonDocument.Parse($$"""{"accountKey":"{{account}}","positions":[]}""");
        return TeamResearchRouter
            .Candidates(evidence.RootElement, news, history, now, false)
            .Any(candidate => candidate.Specialist == TeamRole.News);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
