using System.Net;

namespace Portfolio.Checks;

public sealed class TelegramHttpHandler : HttpMessageHandler
{
    public string Response { get; set; } = "{\"ok\":true,\"result\":true}";

    public string? RequestBody { get; private set; }

    public Uri? RequestAddress { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        this.RequestAddress = request.RequestUri;
        this.RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent(this.Response) };
    }
}
