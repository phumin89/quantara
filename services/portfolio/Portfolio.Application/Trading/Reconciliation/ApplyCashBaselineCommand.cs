using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record ApplyCashBaselineCommand(string Proof, string Acknowledgement)
    : ICommand<CashBaselineReviewResult>;
