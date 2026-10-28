namespace Quantara.Portfolio.Application;

public sealed record CompanyCapitalReadiness(bool CanSimulate, IReadOnlyList<string> Reasons)
{
    public bool CanUseForLiveDecisions { get; }
}
