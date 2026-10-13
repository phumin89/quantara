using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;

namespace Portfolio.Checks;

public static class AccountEvidenceChecks
{
    public static async Task Run()
    {
        var observedAt = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var handler = new GetAccountEvidenceHandler(
            new FakeAccountEvidenceReader([new Balance("THB", 1000m, 700m), new Balance("BTC", 0.1m, 0.1m)]),
            new Quantara.Portfolio.Infrastructure.AccountContext("account-1"),
            new FixedTimeProvider(observedAt)
        );
        var evidence = await handler.Handle(new GetAccountEvidenceQuery(), CancellationToken.None);
        if (
            evidence.AccountKey != "account-1"
            || evidence.ObservedAt != observedAt
            || evidence.Balances.Single(balance => balance.Symbol == "THB").Available != 700m
            || evidence.Fee.Rate != 0.0025m
            || evidence.Fee.Status != "estimated"
            || !evidence.Fee.Source.Contains("not verified", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException("Account evidence must preserve fresh balances and fee uncertainty.");
        }

        Console.WriteLine("Passed: account evidence is read-only, account-scoped, timestamped and fee-explicit");
    }
}
