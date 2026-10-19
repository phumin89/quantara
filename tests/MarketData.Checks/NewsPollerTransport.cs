using Quantara.MarketData.Infrastructure.News;

namespace Quantara.MarketData.Checks;

public sealed class NewsPollerTransport : INewsTransport
{
    private int fedCalls;

    private int blsCalls;

    private bool conditional;

    public int FedCalls => Volatile.Read(ref this.fedCalls);

    public int BlsCalls => Volatile.Read(ref this.blsCalls);

    public bool Conditional => Volatile.Read(ref this.conditional);

    public Task<NewsTransportResponse> Fetch(
        Uri uri,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (uri.Host == "www.federalreserve.gov")
        {
            Interlocked.Increment(ref this.fedCalls);
            Volatile.Write(ref this.conditional, etag == "etag-2" && lastModified == "modified-2");
            return Task.FromResult(new NewsTransportResponse(304, null, null, null, null));
        }

        Interlocked.Increment(ref this.blsCalls);
        return Task.FromResult(new NewsTransportResponse(429, null, null, null, "120"));
    }
}
