using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskOverview(
    string AccountKey,
    string PolicyHash,
    RiskPolicy Policy,
    RiskCapitalView? Capital,
    RiskPerformanceView? Performance,
    string? RefreshError,
    bool ManualOnly = true
);
