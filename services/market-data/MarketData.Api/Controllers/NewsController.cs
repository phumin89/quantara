using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application.News;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/news")]
public sealed class NewsController : ControllerBase
{
    private readonly IMediator mediator;

    public NewsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<NewsPacket>> Get(
        [FromQuery] string? symbols,
        [FromQuery] DateTimeOffset? asOf,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var packet = await this.mediator.Send(new ReadNewsQuery(symbols, asOf), cancellationToken);
            return this.Ok(packet);
        }
        catch (ArgumentException)
        {
            return this.BadRequest(new ProblemDetails { Status = 400, Title = "Invalid news scope or cutoff." });
        }
    }
}
