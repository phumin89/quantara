using System.Text.Json;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public static class DailyLearning
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static LearningSnapshot? Capture(string evidence)
    {
        using var document = JsonDocument.Parse(evidence);
        if (
            !document.RootElement.TryGetProperty("marketResearch", out var market)
            || !market.TryGetProperty("available", out var available)
            || !available.GetBoolean()
            || !market.TryGetProperty("scan", out var scan)
        )
        {
            return null;
        }

        var symbols = scan.GetProperty("symbols");
        var prices = scan.GetProperty("last");
        var usable = scan.GetProperty("usable");
        if (
            symbols.GetArrayLength() > 1000
            || symbols.GetArrayLength() != prices.GetArrayLength()
            || symbols.GetArrayLength() != usable.GetArrayLength()
        )
        {
            throw new InvalidDataException("Learning scan columns are inconsistent.");
        }

        var quotes = new List<LearningQuote>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < symbols.GetArrayLength(); index++)
        {
            var symbol = symbols[index].GetString();
            if (
                symbol is null
                || symbol.Length is < 5 or > 34
                || !symbol.EndsWith("_THB", StringComparison.Ordinal)
                || !symbol[..^4].All(char.IsAsciiLetterOrDigit)
                || !unique.Add(symbol)
            )
            {
                throw new InvalidDataException("Learning market symbol is invalid or duplicated.");
            }

            if (usable[index].GetBoolean() && prices[index].TryGetDecimal(out var price) && price > 0)
            {
                quotes.Add(new LearningQuote(symbol, price));
            }
        }

        var observedAt = market.GetProperty("fetchedAt").GetDateTimeOffset();
        var now = DateTimeOffset.UtcNow;
        return observedAt <= now.AddSeconds(5) && observedAt >= now.AddMinutes(-10)
            ? new LearningSnapshot(observedAt, quotes, observedAt.AddHours(1))
            : null;
    }

    public static LearningEvaluation? Evaluate(TeamRun run, IReadOnlyList<TeamRun> observations, TeamRun current)
    {
        if (run.LearningEvaluation is not null)
        {
            return run.LearningEvaluation;
        }

        if (run.Learning?.EvaluationAt is not DateTimeOffset target)
        {
            return null;
        }

        var source = observations
            .Append(current)
            .Where(sample =>
                sample.AccountKey == run.AccountKey
                && sample.Id != run.Id
                && sample.Learning is not null
                && sample.Learning.ObservedAt >= target
                && sample.Learning.ObservedAt <= target.AddMinutes(15)
            )
            .OrderBy(sample => sample.Learning?.ObservedAt)
            .ThenBy(sample => sample.Id)
            .FirstOrDefault();
        return source?.Learning is LearningSnapshot snapshot
            ? new LearningEvaluation(target, snapshot.ObservedAt, source.Id, snapshot.Markets)
            : null;
    }

    public static string Review(
        string accountKey,
        DateTimeOffset before,
        LearningSnapshot? current,
        IReadOnlyList<TeamRun> history,
        int maxCharacters = 10000
    )
    {
        var day = DateOnly.FromDateTime(before.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var prior = history
            .Where(run =>
                run.AccountKey == accountKey
                && run.Status == TeamRunStatus.Completed
                && run.CreatedAt < before
                && run.Learning is not null
                && run.Learning.ObservedAt < before
                && DateOnly.FromDateTime(run.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime) >= day.AddDays(-6)
            )
            .OrderByDescending(run => run.CreatedAt)
            .ThenByDescending(run => run.Id)
            .ToArray();
        foreach (var detailLimit in new[] { 32, 14, 7, 3, 1, 0 })
        {
            var detailedIds = prior.Take(detailLimit).Select(run => run.Id).ToHashSet();
            var result = JsonSerializer.Serialize(
                new
                {
                    available = current is not null,
                    dailyNetProfitTargetThb = TradingMandate.DailyNetProfitTargetThb,
                    calendarTimeZone = TradingMandate.CalendarTimeZone,
                    realizedNetProfitThb = (decimal?)null,
                    evaluationHorizonMinutes = 60,
                    endpointToleranceMinutes = 15,
                    sourceRoundCount = prior.Length,
                    omittedRoundDetailCount = Math.Max(0, prior.Length - detailLimit),
                    limitations = "All completed rounds counted. One-hour reference outcome frozen using first observation within 15 minutes after deadline; missing/legacy endpoint Unknown. Reference moves are not fills, net/realized profit, missed profit or evidence of edge. Daily net PnL Unknown without full fill/cost reconciliation.",
                    days = prior
                        .GroupBy(run => DateOnly.FromDateTime(run.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime))
                        .Select(group => new
                        {
                            day = group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                            calendarDayEnded = group.Key < day,
                            roundCount = group.Count(),
                            buyProposalCount = group.Count(run => run.Decision == LearningDecision.BuyProposal),
                            sellProposalCount = group.Count(run => run.Decision == LearningDecision.SellProposal),
                            noProposalCount = group.Count(run => run.Decision == LearningDecision.NoProposal),
                            unknownDecisionCount = group.Count(run => run.Decision is null),
                            evaluatedRoundCount = group.Count(run => run.LearningEvaluation is not null),
                            pendingRoundCount = group.Count(run =>
                                run.LearningEvaluation is null
                                && run.Learning?.EvaluationAt is DateTimeOffset deadline
                                && before <= deadline.AddMinutes(15)
                            ),
                            unknownRoundCount = group.Count(run =>
                                run.LearningEvaluation is null
                                && (
                                    run.Learning?.EvaluationAt is not DateTimeOffset deadline
                                    || before > deadline.AddMinutes(15)
                                )
                            ),
                            omittedRoundDetailCount = group.Count(run => !detailedIds.Contains(run.Id)),
                            rounds = group
                                .Where(run => detailedIds.Contains(run.Id))
                                .Select(run => DailyLearning.Round(run)),
                        }),
                },
                DailyLearning.jsonOptions
            );
            if (result.Length <= maxCharacters)
            {
                return result;
            }
        }

        throw new InvalidDataException("Learning context budget is too small.");
    }

    private static object Round(TeamRun run)
    {
        var snapshot = run.Learning ?? throw new InvalidDataException("Learning evidence is missing.");
        var evaluation = run.LearningEvaluation;
        var prices =
            evaluation?.Markets.ToDictionary(quote => quote.Symbol, quote => quote.Price, StringComparer.Ordinal)
            ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
        var matched = snapshot.Markets.Where(quote => quote.Price > 0 && prices.ContainsKey(quote.Symbol)).ToArray();
        return new
        {
            runId = run.Id,
            decision = run.Decision.ToString(),
            observedAt = snapshot.ObservedAt,
            evaluationAt = snapshot.EvaluationAt,
            evaluatedAt = evaluation?.ObservedAt,
            evaluationSourceRunId = evaluation?.SourceRunId,
            sourceMarketCount = snapshot.Markets.Count,
            comparedMarketCount = matched.Length,
            lesson = run.Lesson,
            lessonUnavailable = run.Lesson is null,
            outcomes = matched
                .Select(quote => new
                {
                    symbol = quote.Symbol,
                    beforePrice = quote.Price,
                    laterPrice = prices[quote.Symbol],
                    grossReferenceMovePercent = DailyLearning.PriceMove(quote.Price, prices[quote.Symbol]),
                })
                .Where(outcome => outcome.grossReferenceMovePercent is not null)
                .OrderByDescending(outcome => Math.Abs(outcome.grossReferenceMovePercent ?? 0m))
                .ThenBy(outcome => outcome.symbol)
                .Take(5),
        };
    }

    private static decimal? PriceMove(decimal prior, decimal later)
    {
        try
        {
            return decimal.Round((later - prior) / prior * 100m, 4);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
