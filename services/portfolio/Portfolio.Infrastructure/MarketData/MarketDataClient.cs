using System.Net.Http.Json;
using System.Text.Json;
using Quantara.Portfolio.Application;
using Quantara.Portfolio.Domain;
namespace Quantara.Portfolio.Infrastructure;

public sealed class MarketDataClient(HttpClient http) : IMarketDataClient
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };

    public async Task<Coin[]> GetCoins(CancellationToken cancellationToken)
    {
        var coins = await http.GetFromJsonAsync<MarketCoinResponse[]>("/api/market-data/coins", options, cancellationToken)
            ?? throw new InvalidDataException("Market data returned no coins.");
        if (coins.Any(coin => string.IsNullOrWhiteSpace(coin.Symbol) || string.IsNullOrWhiteSpace(coin.Name) || string.IsNullOrWhiteSpace(coin.Source))
            || coins.DistinctBy(coin => coin.Symbol).Count() != coins.Length)
        {
            throw new InvalidDataException("Market data returned invalid coins.");
        }
        return coins.Select(coin => new Coin(coin.Symbol, coin.Name, coin.Source)).ToArray();
    }

    public async Task<Quote[]> GetQuotes(CancellationToken cancellationToken)
    {
        var quotes = await http.GetFromJsonAsync<MarketQuoteResponse[]>("/api/market-data/quotes", options, cancellationToken)
            ?? throw new InvalidDataException("Market data returned no quotes.");
        if (quotes.Any(quote => string.IsNullOrWhiteSpace(quote.Symbol) || quote.Price <= 0)
            || quotes.DistinctBy(quote => quote.Symbol).Count() != quotes.Length)
        {
            throw new InvalidDataException("Market data returned invalid quotes.");
        }
        return quotes.Select(quote => new Quote(quote.Symbol, quote.Price)).ToArray();
    }
}
