using System.Net.Http.Json;
using Quantara.Team.Application;

namespace Quantara.Team.Infrastructure;

public sealed class MarketResearchReader(HttpClient http) : IMarketResearchReader
{
    public async Task<string> Read(string accountKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/market/research");
        request.Headers.Add("X-Account-Key", accountKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= 40000
            ? body
            : throw new InvalidOperationException("Market research snapshot is too large.");
    }
}
