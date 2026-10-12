using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;
namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto")]
public sealed class PortfolioController : ControllerBase
{
    private readonly IMediator mediator;

    public PortfolioController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("portfolio")]
    public Task<PortfolioView> Get(CancellationToken cancellationToken) => this.mediator.Send(new GetPortfolioQuery(), cancellationToken).AsTask();

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        await this.mediator.Send(new RefreshPortfolioCommand(), cancellationToken);
        return this.Accepted();
    }
}
