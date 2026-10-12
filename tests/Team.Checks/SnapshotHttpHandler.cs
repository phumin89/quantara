using System.Net;
using System.Text;

namespace Team.Checks;

public sealed class SnapshotHttpHandler : HttpMessageHandler
{
    private readonly string json;

    public SnapshotHttpHandler(string json)
    {
        this.json = json;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.json, Encoding.UTF8, "application/json"),
            }
        );
    }
}
