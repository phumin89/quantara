using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market-data/coins")]
public sealed class CoinsController : ControllerBase
{
    private readonly IMediator mediator;

    public CoinsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketCoinResult>>> Get(CancellationToken cancellationToken)
        => this.Ok(await this.mediator.Send(new GetMarketCoinsQuery(), cancellationToken));
}
