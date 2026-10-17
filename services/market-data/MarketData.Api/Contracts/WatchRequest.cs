namespace Quantara.MarketData.Api.Contracts;

public sealed record WatchRequest(IReadOnlyList<string> Symbols);
