using System.Net;

namespace Portfolio.Checks;

public sealed class TradingEventSourceStallingContent : HttpContent
{
    public TradingEventSourceStallingContent()
    {
        this.Headers.ContentType = new("text/event-stream");
    }

    protected override Task<Stream> CreateContentReadStreamAsync() =>
        Task.FromResult<Stream>(new TradingEventSourceStallingStream());

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
}
