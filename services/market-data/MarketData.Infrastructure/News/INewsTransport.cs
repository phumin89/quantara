namespace Quantara.MarketData.Infrastructure.News;

public interface INewsTransport
{
    public Task<NewsTransportResponse> Fetch(
        Uri uri,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken
    );
}
