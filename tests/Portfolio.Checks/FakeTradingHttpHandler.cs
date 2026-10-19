using System.Net;
using System.Net.Http;

namespace Portfolio.Checks;

public sealed class FakeTradingHttpHandler : HttpMessageHandler
{
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    public string ResponseJson { get; init; } = "{\"ok\":true,\"result\":{\"message_id\":42}}";

    public bool ThrowTimeout { get; init; }

    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (this.ThrowTimeout)
        {
            throw new TaskCanceledException("timeout fixture");
        }

        this.Calls++;
        return Task.FromResult(
            new HttpResponseMessage(this.StatusCode) { Content = new StringContent(this.ResponseJson) }
        );
    }
}
