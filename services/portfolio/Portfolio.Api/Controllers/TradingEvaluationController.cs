using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/trading/evaluation")]
public sealed class TradingEvaluationController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? symbols, CancellationToken cancellationToken)
    {
        var requested = (symbols ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (requested.Length is 0 or > 4)
        {
            return this.BadRequest("Provide one to four symbols.");
        }

        try
        {
            return this.Ok(await mediator.Send(new GetTradingEvaluationQuery(requested), cancellationToken));
        }
        catch (ArgumentException)
        {
            return this.BadRequest(
                "Evaluation requires unique uppercase symbols and the v2 strategy. It never changes live rules."
            );
        }
    }
}
