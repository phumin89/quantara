using System.Net;
using System.Net.Http.Json;

namespace Team.Checks;

public sealed class SignalHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

    public SignalHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        this.responder = responder;
    }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        this.Requests += 1;
        return Task.FromResult(this.responder(request));
    }

    public static HttpResponseMessage Json(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
    }
}
