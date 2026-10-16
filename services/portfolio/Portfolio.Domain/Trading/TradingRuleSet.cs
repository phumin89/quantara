namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingRuleSet
{
    public string Version { get; init; } = "ema20-50-breakout-v1";

    public string[] Symbols { get; init; } = ["BTC"];

    public decimal AllocatedThb { get; init; } = 1000m;

    public decimal PerOrderThb { get; init; } = 100m;

    public decimal DailyLossThb { get; init; } = 100m;

    public decimal MaxSpreadBps { get; init; } = 50m;

    public decimal SlippageBps { get; init; } = 20m;

    public decimal TakeProfitBps { get; init; } = 200m;

    public decimal StopLossBps { get; init; } = 100m;

    public decimal TrailingStopBps { get; init; } = 100m;

    public decimal FeeBps { get; init; } = 25m;

    public decimal VolumeRatio { get; init; } = 1.5m;

    public int MaxPositions { get; init; } = 2;

    public int MaxDataAgeSeconds { get; init; } = 90;

    public int TimeExitMinutes { get; init; } = 120;

    public int CooldownMinutes { get; init; } = 15;

    public int OrderLifetimeMinutes { get; init; } = 15;

    public AdaptiveTradingPolicy? Adaptive { get; init; }

    public void Validate()
    {
        if (
            this.Version is not ("ema20-50-breakout-v1" or "adaptive-intraday-v1")
            || (this.Version == "adaptive-intraday-v1") != (this.Adaptive is not null)
            || this.Symbols is null
            || this.Symbols.Length > 16
            || this.Version == "ema20-50-breakout-v1" && this.Symbols.Length == 0
            || this.Version == "adaptive-intraday-v1" && this.Symbols.Length != 0
            || this.Symbols.Any(symbol =>
                string.IsNullOrEmpty(symbol)
                || symbol.Length > 30
                || symbol.Any(c => c is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            )
            || this.Symbols.Distinct(StringComparer.Ordinal).Count() != this.Symbols.Length
        )
        {
            throw new ArgumentException("Invalid trading rule symbols or version.");
        }

        this.Adaptive?.Validate();
        if (
            this.Adaptive is { } adaptive
            && (
                adaptive.Risk.EstimatedFeeRate != this.FeeBps / 10000m
                || adaptive.Risk.MaximumSpreadFraction != this.MaxSpreadBps / 10000m
                || adaptive.Risk.MaximumDataAgeSeconds != this.MaxDataAgeSeconds
            )
        )
        {
            throw new ArgumentException("Adaptive risk and execution fee, spread and freshness limits must match.");
        }

        if (
            this.AllocatedThb is <= 0m or > 1000000000m
            || this.PerOrderThb <= 0m
            || this.PerOrderThb > this.AllocatedThb
            || this.DailyLossThb <= 0m
            || this.DailyLossThb > this.AllocatedThb
            || this.MaxPositions is < 1 or > 16
            || this.MaxDataAgeSeconds is < 10 or > 300
            || this.TimeExitMinutes is < 1 or > 1440
            || this.CooldownMinutes is < 0 or > 1440
            || this.OrderLifetimeMinutes is < 1 or > 1440
            || this.FeeBps is < 0m or > 100m
            || this.MaxSpreadBps is < 0m or > 1000m
            || this.SlippageBps is < 0m or > 1000m
            || this.TakeProfitBps is < 1m or > 10000m
            || this.StopLossBps is < 1m or > 10000m
            || this.TrailingStopBps is < 1m or > 10000m
            || this.VolumeRatio is < 1m or > 10m
        )
        {
            throw new ArgumentException("Invalid trading rule limits.");
        }
    }
}
