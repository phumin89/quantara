namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingExitPlan
{
    public string Version { get; init; } = "adaptive-exit-v1";

    public Guid EntryIntentId { get; init; }

    public string RulesHash { get; init; } = "";

    public DateTimeOffset PreparedAt { get; init; }

    public DateTimeOffset EvidenceAt { get; init; }

    public long EntryCandle { get; init; }

    public AdaptiveExitPolicy Policy { get; init; } = new();

    public decimal FeeBps { get; init; }

    public decimal SlippageBps { get; init; }

    public int MaximumDataAgeSeconds { get; init; } = 90;

    public TradingExitRegime Regime { get; init; }

    public TradingExitRegime PendingRegime { get; init; }

    public int RegimeEvidenceCount { get; init; }

    public int TimeframeSeconds { get; init; } = 300;

    public DateTimeOffset EnteredAt { get; init; }

    public DateTimeOffset NextReviewAt { get; init; }

    public DateTimeOffset HardExitAt { get; init; }

    public decimal EntryAtr { get; init; }

    public decimal CurrentAtr { get; init; }

    public decimal EffectiveStop { get; init; }

    public decimal TargetPrice { get; init; }

    public bool TrailingActive { get; init; }

    public bool Anchored { get; init; }

    public int ReviewCount { get; init; }

    public int NoProgressCount { get; init; }

    public long LastReviewedCandle { get; init; }

    public int Revision { get; init; } = 1;

    public string LastReason { get; init; } = "Entry plan prepared";

    public string? ExitReason { get; init; }

    public void Validate()
    {
        this.Policy.Validate();
        if (
            this.Version != "adaptive-exit-v1"
            || this.EntryIntentId == Guid.Empty
            || this.RulesHash.Length != 64
            || this.RulesHash.Any(character => !Uri.IsHexDigit(character))
            || !Enum.IsDefined(this.Regime)
            || !Enum.IsDefined(this.PendingRegime)
            || this.RegimeEvidenceCount is < 0 or > 1
            || !TradingTimeframes.Supported(this.TimeframeSeconds)
            || this.PreparedAt == default
            || this.EvidenceAt > this.PreparedAt
            || this.PreparedAt - this.EvidenceAt > TimeSpan.FromSeconds(this.MaximumDataAgeSeconds)
            || this.EntryCandle <= 0
            || this.EntryCandle % this.TimeframeSeconds != 0
            || this.EntryAtr <= 0
            || this.CurrentAtr <= 0
            || this.EffectiveStop <= 0
            || this.TargetPrice <= 0
            || this.HardExitAt <= this.EnteredAt
            || this.HardExitAt - this.EnteredAt > TimeSpan.FromDays(1)
            || this.NextReviewAt < this.EnteredAt
            || this.NextReviewAt > this.HardExitAt
            || this.ReviewCount < 0
            || this.NoProgressCount < 0
            || this.Revision < 1
            || this.FeeBps is < 0 or > 100
            || this.SlippageBps is < 0 or > 1000
            || this.MaximumDataAgeSeconds is < 10 or > 300
        )
        {
            throw new ArgumentException("The installed exit plan is incomplete or inconsistent.");
        }
    }
}
