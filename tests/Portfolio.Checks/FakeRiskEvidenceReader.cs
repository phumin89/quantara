using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeRiskEvidenceReader(RiskAccountEvidence evidence) : IRiskEvidenceReader
{
    public RiskAccountEvidence Evidence { get; set; } = evidence;

    public TaskCompletionSource? BeforeRead { get; set; }

    public async Task<RiskAccountEvidence> Read(CancellationToken cancellationToken)
    {
        if (this.BeforeRead is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        return this.Evidence;
    }
}
