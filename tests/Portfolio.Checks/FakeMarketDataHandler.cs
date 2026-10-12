using System.Net;
namespace Quantara.Portfolio.Checks;

public sealed class FakeMarketDataHandler : HttpMessageHandler
{
    public bool Fail
    {
        get; set;
    }

    public bool MissingSource
    {
        get; set;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (this.Fail)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        }

        var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI is missing.");
        var json = requestUri.AbsolutePath.EndsWith("/coins", StringComparison.Ordinal)
            ? this.MissingSource ? "[{\"symbol\":\"BTC\",\"name\":\"Bitcoin\"}]" : "[{\"symbol\":\"BTC\",\"name\":\"Bitcoin\",\"source\":\"exchange\"}]"
            : "[{\"symbol\":\"BTC\",\"price\":100.5}]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
