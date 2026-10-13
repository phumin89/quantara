using System.Net;
using System.Text;

namespace Team.Checks;

public sealed class PublisherHttpHandler : HttpMessageHandler
{
    private readonly Guid responseId;

    public PublisherHttpHandler(Guid responseId = default) => this.responseId = responseId;

    public HttpRequestMessage? Request { get; private set; }

    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        this.Request = request;
        this.Calls += 1;
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"id\":\"{this.responseId:D}\"}}", Encoding.UTF8, "application/json"),
            }
        );
    }
}
