using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class PreviewCashBaselineHandler(IReadOnlyCashBaselineReview reviews)
    : IQueryHandler<PreviewCashBaselineQuery, CashBaselineReviewResult>
{
    public async ValueTask<CashBaselineReviewResult> Handle(
        PreviewCashBaselineQuery query,
        CancellationToken cancellationToken
    ) => await reviews.Preview(cancellationToken);
}
