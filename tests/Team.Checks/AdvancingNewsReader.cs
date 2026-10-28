using Quantara.Team.Application;

namespace Team.Checks;

public sealed class AdvancingNewsReader(string value, Action beforeRead) : INewsEvidenceReader
{
    public Task<string> Read(string? symbols, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        beforeRead();
        return Task.FromResult(value);
    }
}
