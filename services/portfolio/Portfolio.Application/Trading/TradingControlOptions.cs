namespace Quantara.Portfolio.Application;

public sealed record TradingControlOptions(string OwnerToken)
{
    public bool LiveActivationAvailable { get; init; }

    public bool AdaptiveExitLiveAvailable { get; init; }
}
