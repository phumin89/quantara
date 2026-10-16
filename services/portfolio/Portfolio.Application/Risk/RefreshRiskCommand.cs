using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record RefreshRiskCommand : ICommand<RiskOverview>;
