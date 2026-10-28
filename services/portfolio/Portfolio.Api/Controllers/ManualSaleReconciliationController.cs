using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

// Both POST routes inherit the existing local-origin and owner-control-token middleware.
[ApiController]
[Route("api/trading/reconciliation/manual-sale")]
public sealed class ManualSaleReconciliationController(IMediator mediator) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ManualSaleReviewResult> Preview(
        ManualSalePreviewRequest request,
        CancellationToken cancellationToken
    ) => await mediator.Send(new PreviewManualSaleQuery(request.Symbol, request.ExecutionId), cancellationToken);

    [HttpPost("apply")]
    public async Task<ManualSaleReviewResult> Apply(
        ManualSaleApplyRequest request,
        CancellationToken cancellationToken
    ) => await mediator.Send(new ApplyManualSaleCommand(request.Proof, request.Acknowledgement), cancellationToken);

    [HttpPost("cash-baseline/preview")]
    public async Task<CashBaselineReviewResult> PreviewCashBaseline(CancellationToken cancellationToken) =>
        await mediator.Send(new PreviewCashBaselineQuery(), cancellationToken);

    [HttpPost("cash-baseline/apply")]
    public async Task<CashBaselineReviewResult> ApplyCashBaseline(
        ManualSaleApplyRequest request,
        CancellationToken cancellationToken
    ) => await mediator.Send(new ApplyCashBaselineCommand(request.Proof, request.Acknowledgement), cancellationToken);
}
