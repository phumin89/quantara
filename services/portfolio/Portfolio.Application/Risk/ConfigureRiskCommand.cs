using Mediator;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record ConfigureRiskCommand(RiskPolicy Policy, string ExpectedPolicyHash) : ICommand<RiskOverview>;
