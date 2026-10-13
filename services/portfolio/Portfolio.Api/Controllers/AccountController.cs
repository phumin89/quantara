using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto/account")]
public sealed class AccountController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Ok(new { accountKey = await mediator.Send(new GetAccountQuery(), cancellationToken) });
}
