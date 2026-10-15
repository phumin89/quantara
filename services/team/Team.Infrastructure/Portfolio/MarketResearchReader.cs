using System.Net.Http.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class MarketResearchReader(HttpClient http) : IMarketResearchReader
{
    public Task<string> Read(string accountKey, CancellationToken cancellationToken) =>
        this.ReadForMarkets(accountKey, [], cancellationToken);

    public async Task<string> ReadForMarkets(
        string accountKey,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken
    )
    {
        var query = symbols.Count == 0 ? "" : "?symbols=" + Uri.EscapeDataString(string.Join(',', symbols));
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/market/research" + query);
        request.Headers.Add("X-Account-Key", accountKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= 40000
            ? body
            : throw new InvalidOperationException("Market research snapshot is too large.");
    }
}
