namespace Quantara.Portfolio.Application;

public interface IRiskEvidenceReader
{
    public Task<RiskAccountEvidence> Read(CancellationToken cancellationToken);
}
