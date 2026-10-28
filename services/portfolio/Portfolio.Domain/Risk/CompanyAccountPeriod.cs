namespace Quantara.Portfolio.Domain.Risk;

public sealed record CompanyAccountPeriod(
    CapitalSnapshot Previous,
    CapitalSnapshot Current,
    IReadOnlyList<CapitalCashFlow> Flows,
    DateTimeOffset CoverageFrom,
    IReadOnlyDictionary<string, string> ExternalFlowEvidence
);
