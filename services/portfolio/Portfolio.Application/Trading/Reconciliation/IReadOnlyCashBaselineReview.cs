namespace Quantara.Portfolio.Application;

public interface IReadOnlyCashBaselineReview
{
    public Task<CashBaselineReviewResult> Preview(CancellationToken cancellationToken);
}
