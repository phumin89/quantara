using Quantara.Team.Application;

namespace Team.Checks;

public sealed class FakeNewsReader(string value) : INewsEvidenceReader
{
    private readonly string packet = value;

    public Task<string> Read(string? symbols, CancellationToken cancellationToken) => Task.FromResult(this.packet);
}
