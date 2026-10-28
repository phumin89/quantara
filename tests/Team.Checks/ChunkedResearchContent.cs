using System.Net;
using System.Text;

namespace Team.Checks;

public sealed class ChunkedResearchContent : HttpContent
{
    private readonly byte[] bytes;

    private readonly Action? onChunk;

    private readonly bool stall;

    public ChunkedResearchContent(string value, int chunkSize = 257, Action? onChunk = null, bool stall = false)
    {
        this.bytes = Encoding.UTF8.GetBytes(value);
        this.ChunkSize = chunkSize;
        this.onChunk = onChunk;
        this.stall = stall;
    }

    public int ChunkSize { get; }

    public int BytesProduced { get; private set; }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        this.SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken
    )
    {
        if (this.stall)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        for (var offset = 0; offset < this.bytes.Length; offset += this.ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(this.ChunkSize, this.bytes.Length - offset);
            await stream.WriteAsync(this.bytes.AsMemory(offset, count), cancellationToken);
            this.BytesProduced += count;
            this.onChunk?.Invoke();
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
