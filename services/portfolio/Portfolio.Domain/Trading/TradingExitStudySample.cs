namespace Quantara.Portfolio.Domain.Trading;

public sealed record TradingExitStudySample
{
    public string SourceMode { get; init; } = "paper";

    public string SourceRulesHash { get; init; } = "";

    public Guid EntryIntentId { get; init; }

    public string Symbol { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public decimal? HypotheticalNetThb { get; init; }

    public decimal? BaselineNetThb { get; init; }

    public DateTimeOffset? HypotheticalExitAt { get; init; }

    public DateTimeOffset? BaselineExitAt { get; init; }

    public string Status { get; init; } = "active";

    public TradingHolding? Holding { get; init; }

    public decimal Quantity { get; init; }

    public decimal EntryCostThb { get; init; }

    public string Assumptions { get; init; } = "";

    public string? Reason { get; init; }

    public TradingExitPlan? ExitPlan { get; init; }
}
