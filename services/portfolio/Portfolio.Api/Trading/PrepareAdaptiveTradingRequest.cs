using System.Text.Json.Serialization;

namespace Quantara.Portfolio.Api;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareAdaptiveTradingRequest(
    [property: JsonRequired] string AccountKey,
    [property: JsonRequired] string RulesHash
);
