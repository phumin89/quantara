using System.Globalization;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Quantara.Team.Application;

namespace Quantara.Team.Api;

[ApiController]
[Route("api/team")]
public sealed class TeamController : ControllerBase
{
    private readonly IMediator mediator;

    private readonly ITeamCoordinator coordinator;

    public TeamController(IMediator mediator, ITeamCoordinator coordinator)
    {
        this.mediator = mediator;
        this.coordinator = coordinator;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var run = await this.mediator.Send(new GetTeamQuery(), cancellationToken);
        var company = await this.mediator.Send(new GetCompanyQuery(), cancellationToken);

        return this.Ok(
            new
            {
                configured = this.coordinator.IsConfigured,
                latestRun = run,
                company,
            }
        );
    }

    [HttpPost("company")]
    public async Task<IActionResult> Company(CompanyControl body, CancellationToken cancellationToken)
    {
        var company = await this.mediator.Send(new SetCompanyEnabledCommand(body.Enabled), cancellationToken);

        return this.Ok(company);
    }

    [HttpGet("brief")]
    public async Task<IActionResult> Brief([FromQuery] string? date, CancellationToken cancellationToken)
    {
        if (
            !DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate
            )
            || parsedDate < new DateOnly(1970, 1, 1)
            || parsedDate > new DateOnly(9999, 12, 30)
        )
        {
            return this.BadRequest(new { error = "Date must use yyyy-MM-dd format." });
        }

        var runs = await this.mediator.Send(new GetDailyBriefQuery(parsedDate), cancellationToken);

        return this.Ok(new DailyBriefResponse(parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), runs));
    }

    [HttpGet("news")]
    public async Task<IActionResult> News([FromQuery] string? symbols, CancellationToken cancellationToken)
    {
        var packet = await this.mediator.Send(new GetTeamNewsQuery(symbols), cancellationToken);
        return this.Content(packet, "application/json");
    }

    [HttpGet("policy-candidate")]
    public async Task<IActionResult> PolicyCandidate(CancellationToken cancellationToken) =>
        this.Ok(await this.mediator.Send(new GetPolicyCandidateQuery(), cancellationToken));

    [HttpPost("runs")]
    public async Task<IActionResult> Start(TeamObjective body, CancellationToken cancellationToken)
    {
        var run = await this.mediator.Send(new CreateTeamRunCommand(body.Objective), cancellationToken);

        return this.Accepted($"/api/team/runs/{run.Id}", run);
    }

    [HttpPost("runs/{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken cancellationToken)
    {
        var run = await this.mediator.Send(new StopTeamRunCommand(id), cancellationToken);

        return run is null ? this.NotFound(new { error = "Team task was not found." }) : this.Ok(run);
    }
}
