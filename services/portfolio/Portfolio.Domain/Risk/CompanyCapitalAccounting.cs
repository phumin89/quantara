namespace Quantara.Portfolio.Domain.Risk;

public static class CompanyCapitalAccounting
{
    public static CompanyCapitalReport Reconcile(
        IReadOnlyList<CompanyAccountPeriod> accounts,
        IReadOnlyList<CompanyTransferLink> transfers,
        IReadOnlyList<string> expectedAccountKeys
    )
    {
        if (accounts.Count is < 1 or > 100 || transfers.Count > 100000)
        {
            throw new InvalidDataException("Company accounting scope is empty or exceeds its evidence budget.");
        }

        var from = accounts[0].Previous.ObservedAt;
        var to = accounts[0].Current.ObservedAt;
        var issues = new List<string>();
        var expected = expectedAccountKeys.ToHashSet(StringComparer.Ordinal);
        if (
            expected.Count is < 1 or > 100
            || expected.Count != expectedAccountKeys.Count
            || expected.Any(string.IsNullOrWhiteSpace)
        )
        {
            throw new InvalidDataException("Configured company account scope is empty, duplicated or invalid.");
        }

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        var flows = new Dictionary<(string Account, string Id), CapitalCashFlow>();
        var external = new HashSet<(string Account, string Id)>();
        foreach (var account in accounts)
        {
            ReadAccount(account, from, to, scopes, flows, external, issues);
        }

        if (scopes.Any(scope => !expected.Contains(scope)))
        {
            throw new InvalidDataException("Observed account is outside the configured company scope.");
        }

        if (!scopes.SetEquals(expected))
        {
            issues.Add("A configured company account has no complete accounting interval.");
        }

        var linked = new HashSet<(string Account, string Id)>();
        var transferCount = 0;
        decimal internalNet = 0;
        decimal? internalFees = 0;
        foreach (var transfer in transfers.Distinct())
        {
            var withdrawalKey = (transfer.WithdrawalAccountKey, transfer.WithdrawalFlowId);
            var depositKey = (transfer.DepositAccountKey, transfer.DepositFlowId);
            if (
                string.IsNullOrWhiteSpace(transfer.EvidenceReference)
                || transfer.WithdrawalAccountKey == transfer.DepositAccountKey
                || !linked.Add(withdrawalKey)
                || !linked.Add(depositKey)
                || external.Contains(withdrawalKey)
                || external.Contains(depositKey)
            )
            {
                throw new InvalidDataException("Internal transfer has missing proof or conflicting classification.");
            }

            if (
                !flows.TryGetValue(withdrawalKey, out var withdrawal) || !flows.TryGetValue(depositKey, out var deposit)
            )
            {
                issues.Add("An internal transfer leg is missing from the scoped accounting interval.");
                continue;
            }

            if (
                withdrawal.Kind != CashFlowKind.Withdrawal
                || deposit.Kind != CashFlowKind.Deposit
                || deposit.FeeQuantity != 0
                || withdrawal.Symbol != deposit.Symbol
                || withdrawal.Quantity - withdrawal.FeeQuantity != deposit.Quantity
            )
            {
                throw new InvalidDataException(
                    "Internal transfer evidence does not reconcile direction, asset or quantity."
                );
            }

            internalNet += deposit.ValueThb.GetValueOrDefault() - withdrawal.ValueThb.GetValueOrDefault();
            if (withdrawal.Symbol == "THB")
            {
                internalFees += withdrawal.FeeQuantity;
            }
            else if (withdrawal.FeeQuantity > 0)
            {
                internalFees = null;
            }

            transferCount++;
        }

        if (flows.Keys.Any(key => !external.Contains(key) && !linked.Contains(key)))
        {
            issues.Add(
                "Capital movement classification is incomplete; unknown movements are not company contributions or income."
            );
        }

        var deposits = external
            .Where(key => flows[key].Kind == CashFlowKind.Deposit)
            .Sum(key => flows[key].ValueThb.GetValueOrDefault());
        var withdrawals = external
            .Where(key => flows[key].Kind == CashFlowKind.Withdrawal)
            .Sum(key => flows[key].ValueThb.GetValueOrDefault());
        var opening = accounts.Sum(account => account.Previous.EquityThb.GetValueOrDefault());
        var closing = accounts.Sum(account => account.Current.EquityThb.GetValueOrDefault());
        var known = issues.Count == 0;
        return new(
            from,
            to,
            expected.Order(StringComparer.Ordinal).ToArray(),
            known ? opening : null,
            known ? closing : null,
            known ? deposits : null,
            known ? withdrawals : null,
            known ? deposits - withdrawals : null,
            known ? internalNet : null,
            known ? internalFees : null,
            known ? closing - opening - deposits + withdrawals : null,
            flows.Count,
            transferCount,
            issues.Distinct(StringComparer.Ordinal).ToArray()
        );
    }

    private static void ReadAccount(
        CompanyAccountPeriod account,
        DateTimeOffset from,
        DateTimeOffset to,
        HashSet<string> scopes,
        Dictionary<(string Account, string Id), CapitalCashFlow> flows,
        HashSet<(string Account, string Id)> external,
        List<string> issues
    )
    {
        var previous = account.Previous;
        var current = account.Current;
        if (
            string.IsNullOrWhiteSpace(current.AccountKey)
            || previous.AccountKey != current.AccountKey
            || current.ObservedAt <= previous.ObservedAt
            || !scopes.Add(current.AccountKey)
        )
        {
            throw new InvalidDataException(
                "Company accounting has duplicate accounts, mismatched identity or invalid chronology."
            );
        }

        if (
            previous.ObservedAt != from
            || current.ObservedAt != to
            || account.CoverageFrom > previous.ObservedAt
            || previous.Source != CapitalSource.Broker
            || current.Source != CapitalSource.Broker
            || previous.Blocker is not null
            || current.Blocker is not null
            || previous.HasUncertainOrders
            || current.HasUncertainOrders
            || previous.EquityThb is null or < 0
            || current.EquityThb is null or < 0
        )
        {
            issues.Add(
                "Complete broker valuations, aligned cutoffs and transfer coverage are required for every scoped account."
            );
        }

        var unique = CapitalFlowEvidence.Unique(account.Flows, current.ObservedAt);
        foreach (var proof in account.ExternalFlowEvidence)
        {
            if (string.IsNullOrWhiteSpace(proof.Value) || !unique.Any(flow => flow.Id == proof.Key))
            {
                throw new InvalidDataException(
                    "External capital classification has missing evidence or an unknown broker identity."
                );
            }
        }

        foreach (var flow in unique.Where(flow => flow.At > previous.ObservedAt && flow.At <= current.ObservedAt))
        {
            var key = (current.AccountKey, flow.Id);
            flows.Add(key, flow);
            if (flow.ValueThb is null)
            {
                issues.Add("A capital movement has no verified THB valuation.");
            }

            if (account.ExternalFlowEvidence.ContainsKey(flow.Id))
            {
                external.Add(key);
            }
        }
    }
}
