using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeRiskEvidenceReader(RiskAccountEvidence evidence) : IRiskEvidenceReader
{
    public RiskEvidenceOrigin Origin { get; set; } = new("bitkub", "TH", "offlineFixture", "bitkub-risk-read-only-v1");

    public int ReadCalls { get; private set; }

    public RiskAccountEvidence Evidence { get; set; } = evidence;

    public TaskCompletionSource? BeforeRead { get; set; }

    public async Task<RiskAccountEvidence> Read(CancellationToken cancellationToken)
    {
        this.ReadCalls++;
        if (this.BeforeRead is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        return this.Evidence;
    }
}
