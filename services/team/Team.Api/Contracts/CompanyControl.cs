using System.Text.Json.Serialization;

namespace Quantara.Team.Api;

public sealed record CompanyControl([property: JsonRequired] bool Enabled);
