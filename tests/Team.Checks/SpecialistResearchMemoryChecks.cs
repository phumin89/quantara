using System.Text.Json;
using Quantara.Team.Application;
using Quantara.Team.Domain;
using Quantara.Team.Infrastructure;

namespace Team.Checks;

public static class SpecialistResearchMemoryChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new ResearchTask(TeamRole.Intraday, new string('c', 64), "policy", now, now.AddMinutes(20));
        var current = new TeamRun(
            Guid.NewGuid(),
            "current fixture",
            TeamRunStatus.Running,
            now,
            now,
            [],
            null,
            new string('a', 64),
            Task: task
        );
        var valid = new TeamRun(
            Guid.NewGuid(),
            "prior fixture",
            TeamRunStatus.Completed,
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, "reviewed fixture", null)],
            null,
            current.AccountKey,
            Lesson: new LearningLesson("Check coverage", "Reported bars had a gap", "Compare the next supplied window"),
            Task: task
        );
        Recall(current, valid, task);
        Exclusions(current, valid, task);
        Ordering(current, valid);
        Compatibility(current, task, now);
        Budgets(current, valid);
        Console.WriteLine(
            "Specialist memory: account/role/policy isolation, lineage, ordering, legacy learning and whole-entry budget compaction passed."
        );
    }

    private static void Recall(TeamRun current, TeamRun valid, ResearchTask task)
    {
        foreach (
            var role in new[] { TeamRole.Intraday, TeamRole.Swing, TeamRole.News, TeamRole.Positions, TeamRole.Quant }
        )
        {
            using var result = Build(
                current with
                {
                    Task = task with { Specialist = role },
                },
                [valid with { Task = task with { Specialist = role } }],
                role: role.ToString().ToLowerInvariant()
            );
            var memory = result.RootElement.GetProperty("specialistMemory");
            Check(
                memory.GetProperty("entries").GetArrayLength() == 1 && memory.GetProperty("available").GetBoolean(),
                "Each specialist receives its prior lesson."
            );
            Check(
                !memory.GetProperty("verified").GetBoolean() && !memory.GetProperty("authority").GetBoolean(),
                "Memory is reported history without outcome proof or authority."
            );
            var entry = memory.GetProperty("entries")[0];
            Check(
                entry.GetProperty("runId").GetGuid() == valid.Id
                    && entry.GetProperty("policyHash").GetString() == task.PolicyHash,
                "Report and policy provenance survive."
            );
            Check(
                entry.GetProperty("lesson").GetProperty("nextTest").GetString() == valid.Lesson?.NextTest,
                "Whole typed lesson has transport casing."
            );
            Check(
                result.RootElement.GetProperty("dailyLearning").ValueKind == JsonValueKind.Null,
                "Specialists avoid unrelated full learning history."
            );
        }
    }

    private static void Exclusions(TeamRun current, TeamRun valid, ResearchTask task)
    {
        var excluded = new[]
        {
            valid with
            {
                Id = Guid.NewGuid(),
                AccountKey = new string('b', 64),
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Task = task with { Specialist = TeamRole.News },
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Task = task with { PolicyHash = "other" },
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Task = task with { PolicyHash = null },
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Task = null,
            },
            valid with
            {
                Id = Guid.NewGuid(),
                CreatedAt = current.CreatedAt.AddSeconds(1),
            },
            valid with
            {
                Id = Guid.NewGuid(),
                UpdatedAt = current.CreatedAt.AddSeconds(1),
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Status = TeamRunStatus.Failed,
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Status = TeamRunStatus.Cancelled,
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Steps = [],
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Steps = [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Running, "unfinished", null)],
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Steps = [new TeamStep(TeamRole.Reviewer, TeamRunStatus.Completed, null, null)],
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Lesson = null,
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Lesson = new LearningLesson(" ", "reported", "next"),
            },
            valid with
            {
                Id = Guid.NewGuid(),
                Lesson = new LearningLesson(new string('x', 501), "reported", "next"),
            },
        };
        using (var result = Build(current, excluded.Append(valid).ToArray()))
        {
            var entries = result.RootElement.GetProperty("specialistMemory").GetProperty("entries");
            Check(
                entries.GetArrayLength() == 1 && entries[0].GetProperty("runId").GetGuid() == valid.Id,
                "Only same account/role/policy completed prior review enters memory."
            );
        }

        using (var result = Build(current, excluded))
        {
            var memory = result.RootElement.GetProperty("specialistMemory");
            Check(
                !memory.GetProperty("available").GetBoolean() && memory.GetProperty("entries").GetArrayLength() == 0,
                "No eligible memory is explicitly unavailable."
            );
        }

        using var matchedNull = Build(
            current with
            {
                Task = task with { PolicyHash = null },
            },
            [valid with { Task = task with { PolicyHash = null } }]
        );
        Check(
            matchedNull.RootElement.GetProperty("specialistMemory").GetProperty("entries").GetArrayLength() == 1,
            "Exact null policy matches are supported."
        );
    }

    private static void Ordering(TeamRun current, TeamRun valid)
    {
        var first = valid with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var second = valid with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var older = valid with { Id = Guid.NewGuid(), CreatedAt = valid.CreatedAt.AddMinutes(-1) };
        using var result = Build(current, [older, first, second]);
        var memory = result.RootElement.GetProperty("specialistMemory");
        var entries = memory.GetProperty("entries");
        Check(
            entries.GetArrayLength() == 2
                && entries[0].GetProperty("runId").GetGuid() == second.Id
                && entries[1].GetProperty("runId").GetGuid() == first.Id,
            "Newest two lessons use stable date/ID ordering."
        );
        Check(
            memory.GetProperty("omittedCount").GetInt32() == 1 && memory.GetRawText().Length <= 4000,
            "Older eligible lessons are counted and bounded."
        );
    }

    private static void Compatibility(TeamRun current, ResearchTask task, DateTimeOffset now)
    {
        foreach (var role in new[] { "ceo", "reviewer" })
        {
            using var result = Build(
                current with
                {
                    Task = task with { Specialist = TeamRole.Ceo },
                    Learning = new LearningSnapshot(now, []),
                },
                [],
                role: role
            );
            Check(
                result.RootElement.GetProperty("dailyLearning").GetProperty("sourceRoundCount").GetInt32() == 0
                    && result.RootElement.GetProperty("specialistMemory").ValueKind == JsonValueKind.Null,
                "Focused CEO and Reviewer retain their broader learning."
            );
        }

        using var legacy = Build(current with { Task = null, Learning = new LearningSnapshot(now, []) }, []);
        Check(
            legacy.RootElement.GetProperty("dailyLearning").GetProperty("sourceRoundCount").GetInt32() == 0,
            "Legacy learning stays intact."
        );
    }

    private static void Budgets(TeamRun current, TeamRun valid)
    {
        using var empty = Build(current, [], "{\"positions\":[],\"padding\":\"\"}");
        var padding = new string(
            'x',
            TeamResearchContext.MaxPortfolioCharacters - empty.RootElement.GetRawText().Length - 100
        );
        var evidence = JsonSerializer.Serialize(new { positions = Array.Empty<object>(), padding });
        var older = valid with { Id = Guid.NewGuid(), CreatedAt = valid.CreatedAt.AddMinutes(-1) };
        using (var result = Build(current, [valid, older], evidence))
        {
            var memory = result.RootElement.GetProperty("specialistMemory");
            Check(
                memory.GetProperty("entries").GetArrayLength() == 0
                    && !memory.GetProperty("available").GetBoolean()
                    && memory.GetProperty("omittedCount").GetInt32() == 2,
                "Whole lessons yield to evidence at the context limit."
            );
            Check(
                result.RootElement.GetProperty("evidence").GetProperty("padding").GetString() == padding,
                "Current evidence is never truncated for memory."
            );
        }

        var escapedLesson = new LearningLesson(new string('"', 500), new string('"', 500), new string('"', 500));
        using (
            var result = Build(
                current,
                [valid with { Lesson = escapedLesson }, older with { Lesson = escapedLesson }],
                JsonSerializer.Serialize(new { positions = Array.Empty<object>(), padding = new string('"', 23000) })
            )
        )
        {
            var wire = CodexWorker.SerializeRequest("intraday", "fixture", [], result.RootElement.GetRawText());
            Check(
                wire.Length <= CodexWorker.MaxRequestCharacters && result.RootElement.GetRawText().Length <= 60000,
                "Escaped memory respects context and wire limits."
            );
            Check(
                result.RootElement.GetProperty("specialistMemory").GetProperty("omittedCount").GetInt32() > 0,
                "Escaping drops whole entries."
            );
        }

        try
        {
            using var oversized = Build(
                current,
                [valid],
                JsonSerializer.Serialize(new { positions = Array.Empty<object>(), padding = new string('x', 70000) })
            );
            throw new InvalidOperationException("Irreducible oversized evidence was accepted.");
        }
        catch (CodexWorkerException error)
            when (error.Message.Contains("worker_input_too_large", StringComparison.Ordinal))
        {
            Check(valid.Lesson?.Lesson == "Check coverage", "Compaction never mutates retained reports.");
        }
    }

    private static JsonDocument Build(
        TeamRun current,
        IReadOnlyList<TeamRun> history,
        string evidence = "{\"positions\":[]}",
        string role = "intraday"
    ) => JsonDocument.Parse(TeamResearchContext.Build(role, "fixture", [], evidence, current, history, null));

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
