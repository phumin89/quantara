using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class StopTeamRunHandler : ICommandHandler<StopTeamRunCommand, TeamRun?>
{
    private readonly ITeamCoordinator coordinator;

    public StopTeamRunHandler(ITeamCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    public async ValueTask<TeamRun?> Handle(StopTeamRunCommand command, CancellationToken cancellationToken)
    {
        return await this.coordinator.RequestStop(command.Id, cancellationToken);
    }
}
