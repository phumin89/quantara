using Quantara.Portfolio.Domain.Risk;
using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Domain.Company;

public sealed record CompanySimulationCycle(
    CompanySimulationRequest Request,
    CompanyCapitalReport? Capital,
    string RulesHash,
    string Status,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CompanySimulationRole> Roles,
    TradingState? State,
    decimal? OpeningPaperEquityThb,
    decimal? ClosingPaperEquityThb,
    decimal? AppliedContributionsThb,
    decimal? AppliedTransferFeesThb,
    decimal? RealizedNetThb,
    decimal? ExecutionFeesThb,
    decimal? NetPaperChangeThb,
    decimal? TargetGapThb
)
{
    public string Protocol { get; } = "company-paper-cycle-v1";

    public bool CanUseForLiveDecisions { get; }

    public bool VerifiedIncome { get; }
}
