namespace TradingCompany.Application;

public static class ForwardPaperFunding
{
    public static ForwardPaperAccount Synchronize(ForwardPaperAccount? paper, ForwardFundingAccount funding,
        ForwardInvestmentAccount? legacy, DateTimeOffset now, decimal initialCapital = 0m)
    {
        if (initialCapital < 0) throw new InvalidDataException("Initial paper capital cannot be negative.");
        ForwardInvestmentReconciliation.Pending(funding, legacy);
        if (now < funding.OpenedAt || funding.Postings.Any(p => p.PostedAt > now) ||
            legacy?.Credits.Any(c => c.CreditedAt > now) == true)
            throw new InvalidDataException("Cannot import future contributions.");
        if (paper is null)
        {
            paper = ForwardPaperLedger.Open(funding.AccountId);
            foreach (var credit in legacy?.Credits ?? []) paper = ForwardPaperLedger.Fund(paper, credit);
        }
        Pending(paper, funding);
        if (initialCapital > 0) paper = ForwardPaperLedger.InitializeCapital(paper, initialCapital, now);
        var existing = paper.Events.Count(e => e.Credit is not null);
        foreach (var source in funding.Postings.Where(p => p.Allocation.Investment > 0).Skip(existing))
            paper = ForwardPaperLedger.Fund(paper, new(source.Allocation.SalaryDate, source.Allocation.Investment, now));
        if (Pending(paper, funding) != 0m) throw new InvalidDataException("Funding does not reconcile.");
        return paper;
    }

    public static decimal Pending(ForwardPaperAccount paper, ForwardFundingAccount funding)
    {
        ForwardFundingRules.Validate(funding);
        ForwardPaperLedger.Validate(paper);
        if (paper.FundingAccountId != funding.AccountId) throw new InvalidDataException("Paper account has another funding source.");
        var credits = paper.Events.Where(e => e.Credit is not null).Select(e => e.Credit!).ToArray();
        var sources = funding.Postings.Where(p => p.Allocation.Investment > 0).ToArray();
        if (credits.Length > sources.Length) throw new InvalidDataException("Treasury history is behind paper contributions.");
        for (var i = 0; i < credits.Length; i++)
            if (credits[i].SalaryDate != sources[i].Allocation.SalaryDate || credits[i].Amount != sources[i].Allocation.Investment ||
                credits[i].CreditedAt < sources[i].PostedAt)
                throw new InvalidDataException("Paper contribution has no matching CFO release.");
        return funding.InvestmentCash - credits.Sum(c => c.Amount);
    }
}
