using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto/risk")]
public sealed class RiskController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new GetRiskOverviewQuery(), cancellationToken));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new RefreshRiskCommand(), cancellationToken));

    [HttpPost("policy")]
    public async Task<IActionResult> Configure(ConfigureRiskRequest request, CancellationToken cancellationToken) =>
        this.Ok(
            await mediator.Send(new ConfigureRiskCommand(request.Policy, request.ExpectedPolicyHash), cancellationToken)
        );
}
