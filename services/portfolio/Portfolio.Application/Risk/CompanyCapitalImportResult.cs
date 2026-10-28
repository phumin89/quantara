using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record CompanyCapitalImportResult(
    string? ImportId,
    long? Revision,
    CompanyCapitalReport? Report,
    CompanyCapitalReadiness Readiness
);
