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
            ? new LearningSnapshot(observedAt, quotes)
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
        var quotes =
            current?.Markets.ToDictionary(quote => quote.Symbol, quote => quote.Price, StringComparer.Ordinal)
            ?? new Dictionary<string, decimal>(StringComparer.Ordinal);
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
            .GroupBy(run => DateOnly.FromDateTime(run.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime))
            .Select(group => group.First())
            .Take(7)
            .ToArray();
        foreach (var count in new[] { 7, 3, 1, 0 })
        {
            var result = JsonSerializer.Serialize(
                new
                {
                    available = current is not null,
                    calendarTimeZone = TradingMandate.CalendarTimeZone,
                    realizedNetProfitThb = (decimal?)null,
                    limitations = "Reference-price observations only, not fills, net returns or proof of a strategy. Realized daily PnL and missed net profit are unknown without complete fill/cost reconciliation. Largest later moves are hindsight diagnostics, never a selection rule or evidence that a trade would have filled.",
                    omittedDayCount = Math.Max(0, prior.Length - count),
                    days = prior
                        .Take(count)
                        .Select(run =>
                        {
                            var snapshot =
                                run.Learning ?? throw new InvalidDataException("Learning evidence is missing.");
                            var review =
                                run.Steps.FirstOrDefault(step =>
                                    step.Role == TeamRole.Reviewer && step.Status == TeamRunStatus.Completed
                                )?.Output
                                ?? "";
                            var comparable =
                                current is not null && current.ObservedAt >= snapshot.ObservedAt.AddMinutes(5);
                            var outcomes = comparable
                                ? snapshot
                                    .Markets.Where(quote => quotes.ContainsKey(quote.Symbol) && quote.Price > 0)
                                    .Select(quote => new
                                    {
                                        symbol = quote.Symbol,
                                        beforePrice = quote.Price,
                                        laterPrice = quotes[quote.Symbol],
                                        grossReferenceMovePercent = DailyLearning.PriceMove(
                                            quote.Price,
                                            quotes[quote.Symbol]
                                        ),
                                    })
                                    .Where(outcome => outcome.grossReferenceMovePercent is not null)
                                    .OrderByDescending(outcome => Math.Abs(outcome.grossReferenceMovePercent ?? 0m))
                                    .ThenBy(outcome => outcome.symbol)
                                    .Take(5)
                                    .ToArray()
                                : [];
                            return new
                            {
                                runId = run.Id,
                                day = DateOnly
                                    .FromDateTime(run.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime)
                                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                                closedDay = DateOnly.FromDateTime(
                                    run.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime
                                ) < day,
                                observedAt = snapshot.ObservedAt,
                                laterObservedAt = current?.ObservedAt,
                                sourceMarketCount = snapshot.Markets.Count,
                                comparedMarketCount = comparable
                                    ? snapshot.Markets.Count(quote => quotes.ContainsKey(quote.Symbol))
                                    : 0,
                                outcomeSelection = "At most five largest absolute later reference moves among previously observed usable markets; descriptive hindsight only.",
                                reviewExcerpt = review.Length <= 1000 ? review : review[^1000..],
                                reviewTruncated = review.Length > 1000,
                                outcomes,
                            };
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
