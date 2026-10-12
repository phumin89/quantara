using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market-data/quotes")]
public sealed class QuotesController : ControllerBase
{
    private readonly IMediator mediator;

    public QuotesController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MarketQuoteResult>>> Get(CancellationToken cancellationToken) =>
        this.Ok(await this.mediator.Send(new GetMarketQuotesQuery(), cancellationToken));
}
