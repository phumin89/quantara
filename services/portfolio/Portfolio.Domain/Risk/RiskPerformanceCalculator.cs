namespace Quantara.Portfolio.Domain.Risk;

public static class RiskPerformanceCalculator
{
    public static RiskPerformance Calculate(
        CapitalSnapshot previous,
        CapitalSnapshot current,
        IReadOnlyList<CapitalCashFlow> flows,
        DateTimeOffset coverageFrom
    )
    {
        if (previous.AccountKey != current.AccountKey || current.ObservedAt <= previous.ObservedAt)
        {
            throw new InvalidDataException("Performance snapshots have different accounts or invalid chronology.");
        }

        var periodFlows = CapitalFlowEvidence
            .Unique(flows, current.ObservedAt)
            .Where(flow => flow.At > previous.ObservedAt)
            .ToArray();
        if (
            previous.EquityThb is null
            || current.EquityThb is null
            || previous.Blocker is not null
            || current.Blocker is not null
            || coverageFrom > previous.ObservedAt
            || periodFlows.Any(flow => flow.ValueThb is null)
        )
        {
            return new(
                previous.ObservedAt,
                current.ObservedAt,
                null,
                null,
                "Unknown: prices or transfer coverage/valuation are incomplete. Deposits and withdrawals are never assumed to be trading profit."
            );
        }

        var external = periodFlows.Sum(flow =>
            flow.Kind == CashFlowKind.Deposit ? flow.ValueThb.GetValueOrDefault() : -flow.ValueThb.GetValueOrDefault()
        );
        var change = current.EquityThb.Value - previous.EquityThb.Value - external;
        // Exact TWR needs valuations at each cash-flow boundary. Never substitute Modified Dietz or a guessed profit.
        decimal? timeWeighted =
            periodFlows.Length == 0 && previous.EquityThb > 0
                ? current.EquityThb.Value / previous.EquityThb.Value - 1
                : null;
        return new(
            previous.ObservedAt,
            current.ObservedAt,
            change,
            timeWeighted,
            periodFlows.Length == 0
                ? "Flow-free interval return from marked equity, including unrealized change; not realized trading profit. Risk is not raised after wins."
                : "Transfers excluded from net equity change. TWR is Unknown without valuations at transfer times; this is not realized trading profit. Risk is not raised after wins."
        );
    }
}
