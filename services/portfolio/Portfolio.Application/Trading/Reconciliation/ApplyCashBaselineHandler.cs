using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class ApplyCashBaselineHandler(ICashBaselineReview reviews)
    : ICommandHandler<ApplyCashBaselineCommand, CashBaselineReviewResult>
{
    public async ValueTask<CashBaselineReviewResult> Handle(
        ApplyCashBaselineCommand command,
        CancellationToken cancellationToken
    ) => await reviews.Apply(command.Proof, command.Acknowledgement, cancellationToken);
}
