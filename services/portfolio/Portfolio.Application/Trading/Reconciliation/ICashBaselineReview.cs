namespace Quantara.Portfolio.Application;

public interface ICashBaselineReview : IReadOnlyCashBaselineReview
{
    public Task<CashBaselineReviewResult> Apply(
        string proof,
        string acknowledgement,
        CancellationToken cancellationToken
    );
}
