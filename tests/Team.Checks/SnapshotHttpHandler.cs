using System.Net;
using System.Text;

namespace Team.Checks;

public sealed class SnapshotHttpHandler : HttpMessageHandler
{
    private readonly string json;

    private readonly string? history;

    private readonly string account;

    public SnapshotHttpHandler(string json, string? history = null, string? account = null)
    {
        this.json = json;
        this.history = history;
        this.account =
            account ?? "{\"accountKey\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri?.AbsolutePath.EndsWith("/account", StringComparison.Ordinal) == true
                            ? this.account
                        : request.RequestUri?.AbsolutePath.EndsWith("/history", StringComparison.Ordinal) == true
                            ? this.history ?? "{}"
                        : this.json,
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
    }
}
