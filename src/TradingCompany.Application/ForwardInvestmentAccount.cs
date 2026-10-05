namespace TradingCompany.Application;

public sealed record ForwardInvestmentCredit(DateOnly SalaryDate, decimal Amount, DateTimeOffset CreditedAt);

// Cash-only investment ledger. No orders or valuation model are enabled in this slice.
public sealed record ForwardInvestmentAccount(int Version, Guid FundingAccountId,
    IReadOnlyList<ForwardInvestmentCredit> Credits)
{
    public decimal TotalContributed => Credits.Sum(c => c.Amount);
    public decimal Cash => TotalContributed;
    public decimal Equity => Cash;
    public decimal TotalPnl => 0m;
    public string Currency => "THB";
    public string TradingStatus => "TRADING_NOT_ENABLED";
}

public interface IForwardInvestmentStore
{
    ForwardInvestmentAccount? Read();
    ForwardInvestmentAccount Synchronize(ForwardFundingAccount funding, DateTimeOffset now);
}

public static class ForwardInvestmentReconciliation
{
    public static decimal Pending(ForwardFundingAccount funding, ForwardInvestmentAccount? portfolio)
    {
        ForwardFundingRules.Validate(funding);
        if (portfolio is null) return funding.InvestmentCash;
        if (portfolio.Version != 1 || portfolio.FundingAccountId != funding.AccountId || portfolio.Credits is null)
            throw new InvalidDataException("Investment portfolio belongs to another treasury.");
        var expected = funding.Postings.Where(p => p.Allocation.Investment > 0).ToArray();
        if (portfolio.Credits.Count > expected.Length) throw new InvalidDataException("Treasury history is behind the portfolio.");
        for (var index = 0; index < portfolio.Credits.Count; index++)
        {
            var credit = portfolio.Credits[index];
            var source = expected[index];
            if (credit is null || credit.SalaryDate != source.Allocation.SalaryDate ||
                credit.Amount != source.Allocation.Investment || credit.CreditedAt < source.PostedAt ||
                (index > 0 && credit.CreditedAt < portfolio.Credits[index - 1].CreditedAt))
                throw new InvalidDataException("Investment credit does not reconcile to approved funding.");
        }
        return funding.InvestmentCash - portfolio.TotalContributed;
    }
}
