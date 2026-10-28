using Quantara.Portfolio.Domain.Risk;

namespace Portfolio.Checks;

public static class CompanyCapitalChecks
{
    public static void Run()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);
        var deposit = new CapitalCashFlow(
            "monthly-capital",
            "THB",
            500,
            0,
            from.AddDays(10),
            CashFlowKind.Deposit,
            500
        );
        var first = Period("a", from, to, 1000, 1530, [deposit, deposit]);
        var report = Reconcile([first], []);
        Check(
            report.DepositsThb == 500 && report.NetEquityChangeThb == 30,
            "Repeated monthly funding is counted once, never as profit."
        );
        Check(
            report.FlowCount == 1 && !report.VerifiedIncome,
            "Reconciliation retains unique evidence without claiming realized income."
        );
        Check(
            CompanyCapitalAccounting.Reconcile([first], [], ["a", "b"]).NetEquityChangeThb is null,
            "An unavailable configured broker account cannot disappear from company totals."
        );
        Rejects(
            () => CompanyCapitalAccounting.Reconcile([first], [], ["b"]),
            "An observed account cannot expand configured company scope."
        );
        Check(
            RiskPerformanceCalculator.Calculate(first.Previous, first.Current, first.Flows, from).NetEquityChangeThb
                == 30,
            "Existing risk performance also deduplicates cash flows."
        );
        Rejects(
            () => Reconcile([first with { Flows = [deposit, deposit with { ValueThb = 700 }] }], []),
            "Conflicting broker identities fail closed."
        );

        var withdrawal = new CapitalCashFlow(
            "transfer-out",
            "THB",
            201,
            1,
            from.AddDays(15),
            CashFlowKind.Withdrawal,
            200
        );
        var incoming = new CapitalCashFlow("transfer-in", "THB", 200, 0, from.AddDays(15), CashFlowKind.Deposit, 200);
        var second = Period("b", from, to, 500, 710, [incoming]) with
        {
            ExternalFlowEvidence = new Dictionary<string, string>(),
        };
        var outgoing = first with { Current = first.Current with { EquityThb = 1329 }, Flows = [deposit, withdrawal] };
        var link = new CompanyTransferLink("a", withdrawal.Id, "b", incoming.Id, "verified-transfer-reference");
        var combined = Reconcile([outgoing, second], [link, link]);
        Check(
            combined.DepositsThb == 500 && combined.WithdrawalsThb == 0,
            "Verified internal transfers do not inflate company contributions or withdrawals."
        );
        Check(
            combined.InternalTransferNetThb == 0
                && combined.InternalTransferFeesThb == 1
                && combined.NetEquityChangeThb == 39,
            "Adapter-shaped net delivery and transfer fees stay separate; consolidated equity retains the fee cost."
        );
        Rejects(
            () => Reconcile([outgoing, second with { Flows = [incoming with { Quantity = 1, ValueThb = 1 }] }], [link]),
            "An unmatched native transfer remainder cannot be accepted as a fee."
        );
        Check(
            combined.FlowCount == 3 && combined.InternalTransferCount == 1,
            "Repeated sync and transfer matches are idempotent."
        );
        Check(
            Reconcile([outgoing, second], []).NetEquityChangeThb is null,
            "Multiple accounts require explicit transfer classification coverage."
        );
        Check(
            Reconcile([outgoing], [link]).NetEquityChangeThb is null,
            "An absent internal-transfer leg blocks company performance."
        );
        Rejects(
            () => Reconcile([outgoing, second], [link, link with { EvidenceReference = "different-proof" }]),
            "A flow cannot be assigned to conflicting transfer matches."
        );
        Rejects(
            () => Reconcile([outgoing, second with { Flows = [incoming with { Symbol = "USD" }] }], [link]),
            "Unverified cross-currency transfer matching fails closed."
        );

        Check(
            Reconcile([first with { CoverageFrom = from.AddDays(1) }], []).DepositsThb is null,
            "Incomplete coverage is unknown, not zero contributions."
        );
        Check(
            Reconcile([first with { Flows = [deposit with { ValueThb = null }] }], []).NetEquityChangeThb is null,
            "Unknown transfer valuation cannot become profit."
        );
        Check(
            Reconcile([first with { Current = first.Current with { HasUncertainOrders = true } }], []).ClosingEquityThb
                is null,
            "Uncertain outcomes cannot certify coherent company equity."
        );
        Check(
            Reconcile(
                [first with { Current = first.Current with { Source = CapitalSource.Paper } }],
                []
            ).NetEquityChangeThb
                is null,
            "Paper capital cannot become company income."
        );
        Rejects(() => Reconcile([first, first], []), "Duplicate account scope is rejected.");
        Rejects(
            () => Reconcile([first with { Flows = [deposit with { Kind = (CashFlowKind)999 }] }], []),
            "Unknown flow kinds are rejected."
        );
        var excluded = deposit with { Id = "before-period", At = from };
        var boundary = Reconcile(
            [Period("a", from, to, 1000, 1530, [deposit, excluded, deposit with { Id = "at-end", At = to }])],
            []
        );
        Check(boundary.DepositsThb == 1000, "Funding intervals exclude opening time and include closing time.");
        Console.WriteLine(
            "Company capital: monthly funding, deduplication/conflicts, scoped transfer matching, costs, coverage, uncertainty and paper isolation passed."
        );
    }

    private static CompanyCapitalReport Reconcile(
        IReadOnlyList<CompanyAccountPeriod> accounts,
        IReadOnlyList<CompanyTransferLink> transfers
    ) =>
        CompanyCapitalAccounting.Reconcile(
            accounts,
            transfers,
            accounts.Select(account => account.Current.AccountKey).Distinct(StringComparer.Ordinal).ToArray()
        );

    private static CompanyAccountPeriod Period(
        string account,
        DateTimeOffset from,
        DateTimeOffset to,
        decimal opening,
        decimal closing,
        IReadOnlyList<CapitalCashFlow> flows
    ) =>
        new(
            Snapshot(account, from, opening),
            Snapshot(account, to, closing),
            flows,
            from,
            flows
                .DistinctBy(flow => flow.Id)
                .ToDictionary(flow => flow.Id, _ => "fixture-external-source", StringComparer.Ordinal)
        );

    private static CapitalSnapshot Snapshot(string account, DateTimeOffset at, decimal equity) =>
        new(account, at, equity, equity, 0, 0, [new("THB", equity, equity, 1, 0)], false, null)
        {
            Source = CapitalSource.Broker,
        };

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Rejects(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
