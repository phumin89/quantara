using System.Net;
using System.Text;

namespace Portfolio.Checks;

public sealed class TradingEventSourceFakeHandler(params string[] bodies) : HttpMessageHandler
{
    private readonly Queue<string> responses = new(bodies);

    public List<string> Headers { get; } = [];

    public string? WatchBody { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (!request.Headers.TryGetValues("X-Quantara-Market-Events", out var values))
        {
            throw new InvalidOperationException("Missing auth.");
        }

        this.Headers.Add(values.Single());
        if (request.Method == HttpMethod.Post)
        {
            this.WatchBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        }

        var body = request.Method == HttpMethod.Get && this.responses.Count > 0 ? this.responses.Dequeue() : "{}";
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            }
        );
    }
}
