using TradingCompany.Application;

namespace TradingCompany.Api;

/// Public crypto snapshots plus opt-in authenticated equity data; never a broker client.
public sealed class ForwardMarketQuotes(BitkubForwardPaperQuotes crypto, AlpacaForwardPaperQuotes equities) : IForwardPaperQuotes
{
    public bool EquityCredentialsConfigured => equities.CredentialsConfigured;
    public IReadOnlyList<string> SupportedSymbols => crypto.SupportedSymbols
        .Concat(EquityCredentialsConfigured ? equities.SupportedSymbols : []).ToArray();

    public Task<ForwardPaperQuote?> GetAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken)
        => crypto.SupportedSymbols.Contains(symbol, StringComparer.Ordinal)
            ? crypto.GetAsync(symbol, now, cancellationToken)
            : equities.GetAsync(symbol, now, cancellationToken);
}
