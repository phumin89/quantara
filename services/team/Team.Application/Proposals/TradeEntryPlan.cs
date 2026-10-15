namespace Quantara.Team.Application;

public sealed record TradeEntryPlan(string TargetPrice, string InvalidationPrice, int ReviewAfterMinutes);
