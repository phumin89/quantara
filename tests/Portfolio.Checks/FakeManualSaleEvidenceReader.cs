using Quantara.Portfolio.Application;

namespace Portfolio.Checks;

public sealed class FakeManualSaleEvidenceReader(ManualSaleBrokerEvidence evidence) : IManualSaleEvidenceReader
{
    public ManualSaleBrokerEvidence Evidence { get; set; } = evidence;

    public int Reads { get; private set; }

    public TaskCompletionSource? BeforeRead { get; set; }

    public async Task<ManualSaleBrokerEvidence> Read(
        string symbol,
        string executionId,
        CancellationToken cancellationToken
    )
    {
        this.Reads++;
        if (this.BeforeRead is { } pending)
        {
            await pending.Task.WaitAsync(cancellationToken);
        }

        return this.Evidence;
    }
}
