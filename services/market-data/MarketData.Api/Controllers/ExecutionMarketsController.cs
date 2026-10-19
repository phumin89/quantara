using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/trading")]
public sealed class ExecutionMarketsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? symbols,
        CancellationToken cancellationToken,
        [FromQuery] bool includeHistory = true,
        [FromQuery] bool adaptiveTimeframes = false
    )
    {
        try
        {
            var requested = symbols?.Split(',', StringSplitOptions.TrimEntries) ?? [];
            return this.Content(
                await mediator.Send(
                    new GetExecutionMarketsQuery(requested, includeHistory, adaptiveTimeframes),
                    cancellationToken
                ),
                "application/json"
            );
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("universe")]
    public async Task<IActionResult> GetUniverse(CancellationToken cancellationToken) =>
        this.Content(await mediator.Send(new GetExecutionUniverseQuery(), cancellationToken), "application/json");

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string symbol,
        [FromQuery] int timeframeSeconds,
        [FromQuery] long from,
        [FromQuery(Name = "to")] long until,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return this.Content(
                await mediator.Send(
                    new GetExecutionHistoryQuery(symbol, timeframeSeconds, from, until),
                    cancellationToken
                ),
                "application/json"
            );
        }
        catch (ArgumentException error)
        {
            return this.BadRequest(new { error = error.Message });
        }
    }
}
