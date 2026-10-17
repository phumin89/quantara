using System.Net;

namespace Portfolio.Checks;

public sealed class TradingEventSourceStallingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content =
                    request.Method == HttpMethod.Get
                        ? new TradingEventSourceStallingContent()
                        : new StringContent("{}"),
            }
        );
    }
}
