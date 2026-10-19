namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed record TradingJobDispatch(string? JobId, string DestinationKey, DateTimeOffset DueAt, int Attempts);
