using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class PreviewManualSaleHandler(IReadOnlyManualSaleReview reviews)
    : IQueryHandler<PreviewManualSaleQuery, ManualSaleReviewResult>
{
    public async ValueTask<ManualSaleReviewResult> Handle(
        PreviewManualSaleQuery query,
        CancellationToken cancellationToken
    ) => await reviews.Preview(query.Symbol, query.ExecutionId, cancellationToken);
}
