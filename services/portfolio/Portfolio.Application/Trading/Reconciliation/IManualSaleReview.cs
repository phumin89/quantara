namespace Quantara.Portfolio.Application;

public interface IManualSaleReview : IReadOnlyManualSaleReview
{
    public Task<ManualSaleReviewResult> Apply(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    );
}
