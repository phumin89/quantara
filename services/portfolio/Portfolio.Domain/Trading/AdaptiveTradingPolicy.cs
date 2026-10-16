using Quantara.Portfolio.Domain.Risk;

namespace Quantara.Portfolio.Domain.Trading;

public sealed record AdaptiveTradingPolicy
{
    public string UniverseVersion { get; init; } = "bitkub-thb-spot-v1";

    public bool IncludeFutureDeposits { get; init; } = true;

    public string[] ExcludedSymbols { get; init; } = ["USDT", "USDC", "DAI"];

    public RiskPolicy Risk { get; init; } = new() { Profile = RiskProfile.Aggressive };

    public decimal DailyLossFraction { get; init; } = 0.1m;

    public decimal DailyProfitTargetThb { get; init; } = 500m;

    public void Validate()
    {
        this.Risk.Validate();
        if (
            this.UniverseVersion != "bitkub-thb-spot-v1"
            || !this.IncludeFutureDeposits
            || this.Risk.Profile != RiskProfile.Aggressive
            || this.ExcludedSymbols is null
            || this.ExcludedSymbols.Length > 64
            || this.ExcludedSymbols.Any(symbol =>
                string.IsNullOrEmpty(symbol)
                || symbol.Length > 30
                || symbol.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
            || this.ExcludedSymbols.Distinct(StringComparer.Ordinal).Count() != this.ExcludedSymbols.Length
            || this.DailyLossFraction is <= 0 or > 1
            || this.DailyProfitTargetThb is <= 0 or > 1000000000m
        )
        {
            throw new ArgumentException("Invalid adaptive trading policy.");
        }
    }
}
