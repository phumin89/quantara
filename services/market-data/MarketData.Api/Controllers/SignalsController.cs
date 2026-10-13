using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/signals")]
public sealed class SignalsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new GetMarketSignalsQuery(), cancellationToken));
}
