using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record ApplyManualSaleCommand(string Proof, string Acknowledgement) : ICommand<ManualSaleReviewResult>;
