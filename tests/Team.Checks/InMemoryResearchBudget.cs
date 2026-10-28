using Quantara.Team.Application;

namespace Team.Checks;

public sealed class InMemoryResearchBudget : IResearchBudget
{
    private readonly object gate = new();

    private readonly List<ResearchBudgetAttempt> claims = [];

    private readonly Dictionary<string, List<ResearchBudgetAttempt>> claimsByAccount = new(StringComparer.Ordinal);

    public IReadOnlyList<ResearchBudgetAttempt> Claims
    {
        get
        {
            lock (this.gate)
            {
                return this.claims.ToArray();
            }
        }
    }

    public Task<ResearchBudgetDecision> TryClaim(
        string accountKey,
        bool hasHeldPosition,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (accountKey.Length != 64 || !accountKey.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Invalid research budget account.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (this.gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.claimsByAccount.TryGetValue(accountKey, out var accountClaims))
            {
                accountClaims = [];
            }

            var decision = ResearchBudgetRules.Decide(accountClaims, hasHeldPosition, now);
            if (decision.Allowed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var claim = new ResearchBudgetAttempt(now, hasHeldPosition);
                accountClaims.Add(claim);
                this.claimsByAccount[accountKey] = accountClaims;
                this.claims.Add(claim);
            }

            return Task.FromResult(decision);
        }
    }
}
