using System.Net;

namespace Quantara.MarketData.Checks;

public sealed class NewsHttpHandler(HttpContent content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
}
