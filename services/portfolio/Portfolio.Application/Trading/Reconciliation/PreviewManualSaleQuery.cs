using Mediator;

namespace Quantara.Portfolio.Application;

public sealed record PreviewManualSaleQuery(string Symbol, string ExecutionId) : IQuery<ManualSaleReviewResult>;
