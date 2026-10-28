using System.Net;

namespace Quantara.MarketData.Checks;

public sealed class UniverseHttpHandler : HttpMessageHandler
{
    public string SymbolsResponse { get; set; } = "{\"error\":11}";

    public string TickerResponse { get; init; } = "{\"error\":11}";

    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        this.RequestCount++;
        var body =
            request.RequestUri?.AbsolutePath.EndsWith("symbols", StringComparison.Ordinal) == true
                ? this.SymbolsResponse
                : this.TickerResponse;
        return Task.FromResult(new HttpResponseMessage(this.StatusCode) { Content = new StringContent(body) });
    }
}
