using System.Text.Json.Serialization;
using Quantara.Portfolio.Domain;

namespace Quantara.Portfolio.Api;

public sealed record CreateOrderRequest(
    [property: JsonRequired] string Symbol,
    [property: JsonRequired] TradeSide Side,
    [property: JsonRequired] string Amount,
    [property: JsonRequired] string LimitPrice,
    [property: JsonRequired] string Reason,
    Guid? SourceRunId = null,
    string? ExpectedAccountKey = null,
    Quantara.Portfolio.Domain.Risk.RiskSignal? RiskSignal = null
);
