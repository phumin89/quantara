using System.Net;
using System.Text;

namespace Portfolio.Checks;

public sealed class TradingMarketTransportContent : HttpContent
{
    private readonly byte[] bytes;

    private readonly Action? onFirstChunk;

    private readonly bool stall;

    private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TradingMarketTransportContent(string body, Action? onFirstChunk = null, bool stall = false)
    {
        this.bytes = Encoding.UTF8.GetBytes(body);
        this.onFirstChunk = onFirstChunk;
        this.stall = stall;
        this.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
    }

    public int BytesProduced { get; private set; }

    public int TotalBytes => this.bytes.Length;

    public bool CancellationObserved { get; private set; }

    public void Release() => this.released.TrySetResult(true);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        this.SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            for (var offset = 0; offset < this.bytes.Length; offset += 65536)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(65536, this.bytes.Length - offset);
                await stream.WriteAsync(this.bytes.AsMemory(offset, count), cancellationToken);
                this.BytesProduced += count;
                if (offset == 0)
                {
                    this.onFirstChunk?.Invoke();
                }
            }

            if (this.stall)
            {
                await this.released.Task.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.CancellationObserved = true;
            throw;
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
