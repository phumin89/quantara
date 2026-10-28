using System.Text;

namespace Quantara.Portfolio.Infrastructure;

public sealed class BoundedSseReader(StreamReader reader)
{
    private readonly char[] buffer = new char[4096];

    private int offset;

    private int length;

    public async Task<string?> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var line = new StringBuilder();
        while (true)
        {
            if (this.offset == this.length)
            {
                this.length = await reader.ReadAsync(this.buffer.AsMemory(), cancellationToken);
                this.offset = 0;
                if (this.length == 0)
                {
                    return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
                }
            }

            var end = Array.IndexOf(this.buffer, '\n', this.offset, this.length - this.offset);
            var count = (end < 0 ? this.length : end) - this.offset;
            if (line.Length + count > 131072)
            {
                throw new InvalidDataException("Market event frame exceeds its budget.");
            }

            line.Append(this.buffer, this.offset, count);
            this.offset += count;
            if (end >= 0)
            {
                this.offset++;
                return line.ToString().TrimEnd('\r');
            }
        }
    }
}
