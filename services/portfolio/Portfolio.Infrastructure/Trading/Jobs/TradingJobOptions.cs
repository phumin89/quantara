namespace Quantara.Portfolio.Infrastructure.Trading.Jobs;

public sealed record TradingJobOptions(string AccountKey, string DestinationKey, bool Enabled)
{
    public bool UseHangfire { get; init; } = true;
}
