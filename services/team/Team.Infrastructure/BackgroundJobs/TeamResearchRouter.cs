using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class TeamResearchRouter
{
    public static IReadOnlyList<ResearchTask> Candidates(
        JsonElement evidence,
        string? news,
        IReadOnlyList<TeamRun> history,
        DateTimeOffset now,
        bool marketTriggered
    )
    {
        var accountKey = evidence.GetProperty("accountKey").GetString();
        var scoped = history.Where(run => run.AccountKey == accountKey && run.CreatedAt <= now).ToArray();
        var tasks = new List<ResearchTask>();
        var held = ResearchEvidence.HeldSymbols(evidence).Count > 0;
        var minimum = marketTriggered ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);
        var newsRevisions = NewsRevisions(news);
        var newsDigest = newsRevisions.Length == 0 ? null : Digest(string.Join('|', newsRevisions));
        var previousNewsTasks = scoped
            .Select(run => run.Task)
            .OfType<ResearchTask>()
            .Where(task => task.Specialist == TeamRole.News)
            .ToArray();
        var coveredNewsRevisions = previousNewsTasks
            .SelectMany(task => task.NewsRevisions ?? [])
            .ToHashSet(StringComparer.Ordinal);
        string? studyDigest = null;
        if (
            evidence.TryGetProperty("tradingPerformance", out var performance)
            && performance.TryGetProperty("evaluation", out var evaluation)
            && evaluation.ValueKind == JsonValueKind.Object
            && evaluation.TryGetProperty("metrics", out var metrics)
            && metrics.ValueKind == JsonValueKind.Array
            && metrics.GetArrayLength() > 0
        )
        {
            studyDigest = Digest(evaluation.GetRawText());
        }

        if (held && Due(TeamRole.Positions, minimum))
        {
            Add(TeamRole.Positions);
        }

        if (Due(TeamRole.Intraday, minimum))
        {
            Add(TeamRole.Intraday);
        }

        if (
            newsDigest is not null
            && Due(TeamRole.News, TimeSpan.FromMinutes(15))
            && !previousNewsTasks.Any(task => task.NewsDigest == newsDigest)
            && newsRevisions.Any(revision => !coveredNewsRevisions.Contains(revision))
        )
        {
            Add(TeamRole.News);
        }

        if (
            evidence.TryGetProperty("marketResearch", out var market)
            && market.TryGetProperty("historicalCandles", out var daily)
            && daily.ValueKind == JsonValueKind.Object
            && daily.TryGetProperty("candles", out var candles)
            && candles.ValueKind == JsonValueKind.Array
            && candles.GetArrayLength() > 0
            && Due(TeamRole.Swing, TimeSpan.FromHours(24))
        )
        {
            Add(TeamRole.Swing);
        }

        if (
            studyDigest is not null
            && Due(TeamRole.Quant, TimeSpan.FromHours(24))
            && !scoped.Any(run => run.Task?.Specialist == TeamRole.Quant && run.Task.StudyDigest == studyDigest)
        )
        {
            Add(TeamRole.Quant);
        }

        if (Due(TeamRole.Ceo, TimeSpan.FromHours(12)))
        {
            Add(TeamRole.Ceo);
        }

        var priorityTasks = tasks.Where(task => task.Specialist == TeamRole.Positions);
        var intradayTasks = tasks.Where(task => task.Specialist == TeamRole.Intraday);
        var backgroundTasks = tasks
            .Where(task => task.Specialist is not (TeamRole.Positions or TeamRole.Intraday))
            .OrderBy(task =>
                scoped
                    .Where(run => run.Task?.Specialist == task.Specialist)
                    .Select(run => (DateTimeOffset?)run.CreatedAt)
                    .Max()
                ?? DateTimeOffset.MinValue
            )
            .ToArray();
        var lastOrdinary = scoped
            .Where(run => run.Task is not null && run.Task.Specialist != TeamRole.Positions)
            .OrderByDescending(run => run.CreatedAt)
            .FirstOrDefault()
            ?.Task?.Specialist;
        return lastOrdinary == TeamRole.Intraday && backgroundTasks.Length > 0
            ? priorityTasks.Concat(backgroundTasks).Concat(intradayTasks).ToArray()
            : priorityTasks.Concat(intradayTasks).Concat(backgroundTasks).ToArray();

        bool Due(TeamRole role, TimeSpan interval) =>
            !scoped.Any(run => run.Task?.Specialist == role && now - run.CreatedAt < interval);

        void Add(TeamRole role) =>
            tasks.Add(
                new ResearchTask(
                    role,
                    Digest(evidence.GetRawText()),
                    ResearchEvidence.PolicyHash(evidence),
                    now,
                    now.AddMinutes(20),
                    newsDigest,
                    studyDigest,
                    role == TeamRole.News ? newsRevisions : null
                )
            );
    }

    public static string Digest(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string[] NewsRevisions(string? news)
    {
        if (news is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(news);
        if (!document.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return events
            .EnumerateArray()
            .Select(item => item.GetProperty("eventId").GetString() + "@" + item.GetProperty("revision").GetInt32())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
