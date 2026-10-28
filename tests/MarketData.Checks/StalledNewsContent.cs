using System.Net;

namespace Quantara.MarketData.Checks;

public sealed class StalledNewsContent : HttpContent
{
    public bool Started { get; private set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        this.SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken
    )
    {
        this.Started = true;
        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
