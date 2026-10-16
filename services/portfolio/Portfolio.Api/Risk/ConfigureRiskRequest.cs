using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Api;

public sealed record ConfigureRiskRequest(
    [property: JsonRequired] RiskPolicy Policy,
    [property: JsonRequired] string ExpectedPolicyHash
);
