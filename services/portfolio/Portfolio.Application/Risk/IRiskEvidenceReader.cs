namespace Quantara.Portfolio.Application;

public interface IRiskEvidenceReader
{
    public RiskEvidenceOrigin Origin { get; }

    public Task<RiskAccountEvidence> Read(CancellationToken cancellationToken);
}
