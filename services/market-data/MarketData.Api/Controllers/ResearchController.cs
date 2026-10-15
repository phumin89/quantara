using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.MarketData.Application;

namespace Quantara.MarketData.Api.Controllers;

[ApiController]
[Route("api/market/research")]
public sealed class ResearchController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? symbols, CancellationToken cancellationToken)
    {
        try
        {
            if (symbols is not null && string.IsNullOrWhiteSpace(symbols))
            {
                throw new ArgumentException("At least one symbol is required when symbols is provided.");
            }

            var requested = symbols is null ? null : symbols.Split(',', StringSplitOptions.TrimEntries);
            if (requested is not null && requested.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Symbols must not contain empty entries.");
            }

            return this.Content(
                await mediator.Send(new GetMarketResearchQuery(requested), cancellationToken),
                "application/json"
            );
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { error = exception.Message });
        }
    }
}
