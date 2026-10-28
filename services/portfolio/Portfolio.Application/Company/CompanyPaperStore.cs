using Quantara.Portfolio.Domain.Trading;

namespace Quantara.Portfolio.Application;

internal sealed class CompanyPaperStore(TradingState state) : ITradingStore, IAccountContext
{
    private TradingState state = state;

    private readonly string rulesHash = state.RulesHash;

    public string AccountKey => "offline-company-paper";

    public Task<TradingState> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.state);
    }

    public Task<bool> Save(TradingState state, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.state.Revision != expectedRevision)
        {
            return Task.FromResult(false);
        }

        Validate(state, this.rulesHash);
        if (state.Revision != expectedRevision + 1)
        {
            throw new InvalidDataException("Offline Paper state has an invalid revision.");
        }

        this.state = state;
        return Task.FromResult(true);
    }

    internal static void Validate(TradingState state, string rulesHash)
    {
        if (
            state.Mode != TradingMode.Paper
            || state.Mandate is not null
            || state.EmergencyStopped
            || state.RulesHash != rulesHash
            || TradingRuleHash.Compute(state.Rules) != rulesHash
            || state.Live.CashThb != 0
            || state.Live.FundingThb != 0
            || state.Live.Positions.Count != 0
            || state.Live.Orders.Count != 0
            || state.Live.Fills.Count != 0
            || state.Paper.CashThb < 0
            || state.Paper.FundingThb < 0
        )
        {
            throw new InvalidDataException(
                "Company simulation must retain fixed rules and an isolated Paper-only ledger."
            );
        }
    }
}
