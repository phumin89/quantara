namespace Quantara.Team.Application;

public sealed class ResearchBudgetDeferredException(ResearchBudgetDecision decision)
    : InvalidOperationException("Research deferred by the measured round budget.")
{
    public ResearchBudgetDecision Decision { get; } = decision;
}
