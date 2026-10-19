using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Quantara.Team.Application;
using Quantara.Team.Domain;

namespace Quantara.Team.Infrastructure;

public static class TeamResearchContext
{
    public const int MaxPortfolioCharacters = 60000;

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(
        string role,
        string objective,
        IReadOnlyList<(string Role, string Output)> previous,
        string evidence,
        TeamRun current,
        IReadOnlyList<TeamRun> evaluatedHistory,
        TeamRun? prior,
        string? news = null
    )
    {
        using var evidenceDocument = JsonDocument.Parse(evidence);
        var priorReview = prior?.Steps.FirstOrDefault(step => step.Role == TeamRole.Reviewer)?.Output;
        var newsNode = JsonNode.Parse(CompactNews(news))?.AsObject() ?? throw new JsonException();
        while (true)
        {
            using var newsDocument = JsonDocument.Parse(newsNode.ToJsonString(TeamResearchContext.jsonOptions));
            for (var omitPriorReview = false; ; omitPriorReview = true)
            {
                for (var learningBudget = 12000; learningBudget >= 0; )
                {
                    string learning;
                    try
                    {
                        learning = DailyLearning.Review(
                            current.AccountKey ?? throw new InvalidOperationException("Research account is missing."),
                            current.CreatedAt,
                            current.Learning,
                            evaluatedHistory,
                            learningBudget
                        );
                    }
                    catch (InvalidDataException error) when (error.Message == "Learning context budget is too small.")
                    {
                        break;
                    }

                    var context = JsonSerializer.Serialize(
                        new
                        {
                            evidence = evidenceDocument.RootElement,
                            dailyLearning = JsonSerializer.Deserialize<JsonElement>(learning),
                            mandate = new
                            {
                                dailyNetProfitTargetThb = TradingMandate.DailyNetProfitTargetThb,
                                calendarTimeZone = TradingMandate.CalendarTimeZone,
                                acceptFullCapitalLoss = TradingMandate.AcceptFullCapitalLoss,
                            },
                            currentGoal = new
                            {
                                id = PortfolioGoal.Current.Id,
                                targetThb = PortfolioGoal.Current.TargetThb,
                                deadlineExclusive = PortfolioGoal.Current.DeadlineExclusive,
                                includesDeposits = PortfolioGoal.Current.IncludesDeposits,
                                timeZone = PortfolioGoal.Current.TimeZone,
                            },
                            news = newsDocument.RootElement,
                            assessmentContext = ResearchEvidence.Context(
                                current,
                                evidenceDocument.RootElement,
                                newsDocument.RootElement
                            ),
                            priorReview = omitPriorReview ? null : priorReview,
                            priorReviewOmitted = omitPriorReview && priorReview is not null,
                            priorReviewCharacters = priorReview?.Length ?? 0,
                            priorRunId = prior?.Id,
                            priorRunStatus = prior?.Status.ToString(),
                            priorUpdatedAt = prior?.UpdatedAt,
                        },
                        TeamResearchContext.jsonOptions
                    );
                    var wireLength = CodexWorker.SerializeRequest(role, objective, previous, context).Length;
                    if (context.Length <= MaxPortfolioCharacters && wireLength <= CodexWorker.MaxRequestCharacters)
                    {
                        return context;
                    }

                    learningBudget = learning.Length - 1;
                }

                if (omitPriorReview || priorReview is null)
                {
                    break;
                }
            }

            var events = newsNode["events"]?.AsArray();
            if (events is not { Count: > 0 })
            {
                break;
            }

            events.RemoveAt(events.Count - 1);
            newsNode["omittedEventCount"] = (newsNode["omittedEventCount"]?.GetValue<int>() ?? 0) + 1;
        }

        throw new CodexWorkerException("worker_input_too_large");
    }

    private static string UnavailableNews() =>
        JsonSerializer.Serialize(
            new
            {
                schema = "news-v1",
                observedAt = DateTimeOffset.UtcNow,
                executionAuthority = false,
                events = Array.Empty<object>(),
                sourceStates = new[]
                {
                    new
                    {
                        id = "market-data",
                        label = "MarketData",
                        url = "https://market-data.invalid",
                        status = "Unavailable",
                        lastSuccessAt = (DateTimeOffset?)null,
                        nextRetryAt = (DateTimeOffset?)null,
                        reason = "not_requested",
                    },
                },
                omittedEventCount = 0,
                pilot = new
                {
                    startedAt = (DateTimeOffset?)null,
                    lastPollAt = (DateTimeOffset?)null,
                    completed72Hours = false,
                    gapCount = 0,
                },
            }
        );

    private static string CompactNews(string? packet)
    {
        var source = string.IsNullOrWhiteSpace(packet) ? UnavailableNews() : packet;
        try
        {
            var node = JsonNode.Parse(source)?.AsObject() ?? throw new JsonException();
            var events = node["events"]?.AsArray();
            if (events is null)
            {
                return UnavailableNews();
            }

            var omitted = node["omittedEventCount"]?.GetValue<int>() ?? 0;
            while (JsonSerializer.Serialize(node).Length > 4000 && events.Count > 0)
            {
                events.RemoveAt(events.Count - 1);
                omitted++;
            }

            node["omittedEventCount"] = omitted;
            return JsonSerializer.Serialize(node);
        }
        catch (JsonException)
        {
            return UnavailableNews();
        }
    }
}
