namespace Quantara.Portfolio.Application;

public interface IManualSaleEvidenceReader
{
    public Task<ManualSaleBrokerEvidence> Read(string symbol, string executionId, CancellationToken cancellationToken);
}
