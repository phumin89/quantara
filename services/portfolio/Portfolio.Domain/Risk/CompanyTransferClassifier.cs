namespace Quantara.Portfolio.Domain.Risk;

public static class CompanyTransferClassifier
{
    public static CompanyTransferLink[] Match(
        IReadOnlyList<CompanyAccountPeriod> accounts,
        IReadOnlyList<CompanyTransferObservation> observations
    )
    {
        var flows = accounts
            .SelectMany(account =>
                CapitalFlowEvidence
                    .Unique(account.Flows, account.Current.ObservedAt)
                    .Where(flow => flow.At > account.Previous.ObservedAt)
                    .Select(flow => (Account: account.Current.AccountKey, Flow: flow))
            )
            .ToDictionary(item => (item.Account, item.Flow.Id), item => item.Flow);
        var links = new List<CompanyTransferLink>();
        foreach (var group in observations.Distinct().GroupBy(item => item.TransferReference, StringComparer.Ordinal))
        {
            var pair = group.ToArray();
            if (
                pair.Length != 2
                || string.IsNullOrWhiteSpace(group.Key)
                || pair[0].CounterpartyAccountKey != pair[1].AccountKey
                || pair[1].CounterpartyAccountKey != pair[0].AccountKey
                || pair[0].AccountKey == pair[1].AccountKey
                || !flows.TryGetValue((pair[0].AccountKey, pair[0].FlowId), out var first)
                || !flows.TryGetValue((pair[1].AccountKey, pair[1].FlowId), out var second)
                || first.Kind == second.Kind
            )
            {
                continue;
            }

            var withdrawal = first.Kind == CashFlowKind.Withdrawal ? first : second;
            var deposit = first.Kind == CashFlowKind.Deposit ? first : second;
            if (
                withdrawal.Symbol != deposit.Symbol
                || deposit.FeeQuantity != 0
                || withdrawal.Quantity - withdrawal.FeeQuantity != deposit.Quantity
            )
            {
                continue;
            }

            var outgoing = first.Kind == CashFlowKind.Withdrawal ? pair[0] : pair[1];
            var incoming = first.Kind == CashFlowKind.Deposit ? pair[0] : pair[1];
            links.Add(new(outgoing.AccountKey, outgoing.FlowId, incoming.AccountKey, incoming.FlowId, group.Key));
        }

        return links.ToArray();
    }
}
