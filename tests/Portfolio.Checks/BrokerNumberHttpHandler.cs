using System.Net;
using System.Text;

namespace Portfolio.Checks;

public sealed class BrokerNumberHttpHandler(Func<string, string> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing synthetic numeric endpoint.");
        if (request.Method != HttpMethod.Get || uri.Host != "bitkub.invalid")
        {
            throw new InvalidOperationException("Numeric evidence checks permit synthetic GET requests only.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var path = uri.PathAndQuery;
        var body = path == "/api/v3/servertime" ? "1792100000000" : response(path);
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );
    }
}
