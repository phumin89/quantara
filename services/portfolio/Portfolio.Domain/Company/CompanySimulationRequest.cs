namespace Quantara.Portfolio.Domain.Company;

public sealed record CompanySimulationRequest(
    string Id,
    DateTimeOffset CapitalFrom,
    DateTimeOffset CapitalTo,
    string ObjectiveVersion,
    decimal TargetThb,
    DateTimeOffset Deadline,
    IReadOnlyList<CompanySimulationFrame> Frames
);
