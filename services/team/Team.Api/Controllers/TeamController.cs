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

        return this.Ok(new { configured = this.coordinator.IsConfigured, latestRun = run });
    }

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
