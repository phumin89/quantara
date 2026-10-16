using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/trading")]
public sealed class ExecutionMarketsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? symbols, CancellationToken cancellationToken)
    {
        try
        {
            var requested = symbols?.Split(',', StringSplitOptions.TrimEntries) ?? [];
            return this.Content(
                await mediator.Send(new GetExecutionMarketsQuery(requested), cancellationToken),
                "application/json"
            );
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { error = exception.Message });
        }
    }
}
