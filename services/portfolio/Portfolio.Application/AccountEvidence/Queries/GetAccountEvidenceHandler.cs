using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class GetAccountEvidenceHandler(
    IAccountEvidenceReader reader,
    IAccountContext account,
    TimeProvider timeProvider
) : IQueryHandler<GetAccountEvidenceQuery, AccountEvidenceView>
{
    private const decimal estimatedFeeRate = 0.0025m;

    public async ValueTask<AccountEvidenceView> Handle(
        GetAccountEvidenceQuery query,
        CancellationToken cancellationToken
    )
    {
        var balances = await reader.ReadBalances(cancellationToken);
        var evidence = balances
            .Select(balance => new AccountBalanceEvidence(balance.Symbol, balance.Quantity, balance.Available))
            .ToArray();
        return new(
            account.AccountKey,
            timeProvider.GetUtcNow(),
            evidence,
            new(
                estimatedFeeRate,
                "estimated",
                "Published THB standard rate, reviewed 2026-10-13: https://support.bitkub.com/en/solutions/articles?id=151000035191 . Charged after fills; THB rounded up to 2 decimals. Actual account fee/credits not verified; no discount assumed."
            )
        );
    }
}
