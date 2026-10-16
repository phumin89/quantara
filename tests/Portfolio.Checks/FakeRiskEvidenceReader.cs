using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeRiskEvidenceReader(RiskAccountEvidence evidence) : IRiskEvidenceReader
{
    public RiskAccountEvidence Evidence { get; set; } = evidence;

    public Task<RiskAccountEvidence> Read(CancellationToken cancellationToken) => Task.FromResult(this.Evidence);
}
