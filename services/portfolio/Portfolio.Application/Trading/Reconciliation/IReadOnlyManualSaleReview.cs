namespace Quantara.Portfolio.Application;

public interface IReadOnlyManualSaleReview
{
    public Task<ManualSaleReviewResult> Preview(string symbol, string executionId, CancellationToken cancellationToken);
}
