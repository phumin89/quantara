namespace Quantara.Team.Application;

public interface INewsEvidenceReader
{
    public Task<string> Read(string? symbols, CancellationToken cancellationToken);
}
