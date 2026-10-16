using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Application;

public sealed record RiskPolicyState(string PolicyHash, RiskPolicy Policy);
