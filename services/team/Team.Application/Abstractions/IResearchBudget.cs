namespace Quantara.Team.Application;

public interface IResearchBudget
{
    public Task<ResearchBudgetDecision> TryClaim(
        string accountKey,
        bool hasHeldPosition,
        DateTimeOffset now,
        CancellationToken cancellationToken
    );
}
