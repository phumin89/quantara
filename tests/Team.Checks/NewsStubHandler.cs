using System.Net;
using System.Net.Http;
using System.Text;

namespace Team.Checks;

public sealed class NewsStubHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );
}
