using System.Text.Json.Serialization;

namespace Quantara.Portfolio.Api;

public sealed record OrderConfirmation([property: JsonRequired] bool Confirmed);
