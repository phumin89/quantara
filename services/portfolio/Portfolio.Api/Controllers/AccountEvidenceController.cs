using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Portfolio.Application;

namespace Quantara.Portfolio.Api;

[ApiController]
[Route("api/crypto/account/evidence")]
public sealed class AccountEvidenceController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Ok(await mediator.Send(new GetAccountEvidenceQuery(), cancellationToken));
}
