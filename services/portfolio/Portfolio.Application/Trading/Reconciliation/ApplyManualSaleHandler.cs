using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class ApplyManualSaleHandler(IManualSaleReview reviews)
    : ICommandHandler<ApplyManualSaleCommand, ManualSaleReviewResult>
{
    public async ValueTask<ManualSaleReviewResult> Handle(
        ApplyManualSaleCommand command,
        CancellationToken cancellationToken
    ) => await reviews.Apply(command.Proof, command.Acknowledgement, cancellationToken);
}
