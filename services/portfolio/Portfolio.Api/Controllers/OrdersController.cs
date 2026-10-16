using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto/orders")]
public sealed class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Ok(new { proposals = await mediator.Send(new GetOrdersQuery(), cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken) =>
        this.Ok(
            await mediator.Send(
                new CreateOrderCommand(
                    request.Symbol,
                    request.Side,
                    request.Amount,
                    request.LimitPrice,
                    request.Reason,
                    request.SourceRunId,
                    request.ExpectedAccountKey,
                    request.RiskSignal
                ),
                cancellationToken
            )
        );

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        OrderConfirmation confirmation,
        CancellationToken cancellationToken
    ) => this.Ok(await mediator.Send(new ApproveOrderCommand(id, confirmation.Confirmed), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new RejectOrderCommand(id), cancellationToken));

    [HttpPost("{id:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id, CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new ReconcileOrderCommand(id), cancellationToken));
}
