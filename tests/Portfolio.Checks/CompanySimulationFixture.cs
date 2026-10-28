using Quantara.Portfolio.Domain.Company;
using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Portfolio.Checks;

public static class CompanySimulationFixture
{
    public static DateTimeOffset At { get; } = new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);

    public static CompanyBrokerAccount Account { get; } = new("bitkub", "TH", "synthetic-company-a", "offlineFixture");

    public static TradingRuleSet Rules { get; } = new() { DailyLossThb = 1, CooldownMinutes = 0 };

    public static CompanySimulationRequest Request(
        string id,
        DateTimeOffset from,
        DateTimeOffset to,
        bool loss = false
    ) =>
        new(
            id,
            from,
            to,
            "fixture-objective-v1",
            1000000000,
            to.AddYears(1),
            loss
                ? [new(to, [Book(to)]), new(to.AddSeconds(1), [Book(to.AddSeconds(1), 105, 105.1m)])]
                : [new(to, [Book(to)])]
        );

    public static TradingBook Book(DateTimeOffset at, decimal bid = 109.9m, decimal ask = 110m)
    {
        var end = at.ToUnixTimeSeconds() / 300 * 300;
        var bars = Enumerable
            .Range(0, 51)
            .Select(index => new TradingBar(
                end - (51 - index) * 300,
                100,
                index == 50 ? 111 : 101,
                99,
                index == 50 ? 110 : 100,
                index == 50 ? 2 : 1
            ))
            .ToArray();
        return new("BTC", at, bid, ask, 10, 10, bars);
    }

    public static CapitalSnapshot Snapshot(string account, DateTimeOffset at, decimal cash) =>
        new(account, at, cash, cash, 0, 0, [new("THB", cash, cash, 1, 0)], false, null)
        {
            Source = CapitalSource.Broker,
        };

    public static CompanyCapitalImport Import(
        string id,
        CompanyBrokerAccount account,
        DateTimeOffset from,
        DateTimeOffset to,
        decimal opening,
        decimal closing,
        IReadOnlyList<CapitalCashFlow>? flows = null
    ) =>
        new(
            id,
            account,
            "company-capital-fixture-v1",
            "synthetic-retained-observation",
            new(
                Snapshot(account.AccountKey, from, opening),
                Snapshot(account.AccountKey, to, closing),
                flows ?? [],
                from,
                (flows ?? []).ToDictionary(flow => flow.Id, _ => "synthetic-bank-proof", StringComparer.Ordinal)
            ),
            []
        );

    public static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
