namespace TradingCompany.Application;

public interface IForwardPaperQuotes
{
    IReadOnlyList<string> SupportedSymbols { get; }
    Task<ForwardPaperQuote?> GetAsync(string symbol, DateTimeOffset now, CancellationToken cancellationToken);
}
