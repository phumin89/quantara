using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;
namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly IMediator mediator;

    public HistoryController(IMediator mediator) { this.mediator = mediator; }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string coin, CancellationToken cancellationToken)
    {
        if (coin.Length is < 1 or > 30 || !coin.All(char.IsAsciiLetterOrDigit))
        {
            return this.BadRequest(new { error = "Invalid coin symbol." });
        }

        return this.Ok(await this.mediator.Send(new GetCoinHistoryQuery(coin.ToUpperInvariant()), cancellationToken));
    }
}
