using Quantara.Team.Application;

namespace Team.Checks;

public sealed class InMemoryResearchBudget : IResearchBudget
{
    private readonly List<ResearchBudgetAttempt> claims = [];

    public IReadOnlyList<ResearchBudgetAttempt> Claims => this.claims;

    public Task<ResearchBudgetDecision> TryClaim(
        string accountKey,
        bool hasHeldPosition,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var accountClaims = this.claims;
        var decision = ResearchBudgetRules.Decide(accountClaims, hasHeldPosition, now);
        if (decision.Allowed)
        {
            this.claims.Add(new ResearchBudgetAttempt(now, hasHeldPosition));
        }

        return Task.FromResult(decision);
    }
}
