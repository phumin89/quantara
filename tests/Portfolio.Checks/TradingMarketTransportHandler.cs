using System.Net;

namespace Portfolio.Checks;

public sealed class TradingMarketTransportHandler(HttpContent content, string path) : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = request.RequestUri ?? throw new InvalidOperationException("Missing synthetic market address.");
        if (
            request.Method != HttpMethod.Get
            || address.Host != "market.fixture.invalid"
            || address.AbsolutePath != path
        )
        {
            throw new InvalidOperationException("Market transport fixtures accept only their exact synthetic GET.");
        }

        this.Requests++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
