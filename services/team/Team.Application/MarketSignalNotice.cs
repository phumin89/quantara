namespace Quantara.Team.Application;

public sealed record MarketSignalNotice(string Key, string Description, DateTimeOffset CheckedAt);
