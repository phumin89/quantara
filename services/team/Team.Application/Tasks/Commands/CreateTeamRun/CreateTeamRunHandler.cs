using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed class CreateTeamRunHandler : ICommandHandler<CreateTeamRunCommand, TeamRun>
{
    private readonly ITeamCoordinator coordinator;

    public CreateTeamRunHandler(ITeamCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    public async ValueTask<TeamRun> Handle(CreateTeamRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var objective = command.Objective?.Trim();
        if (string.IsNullOrEmpty(objective) || objective.Length > 2000)
        {
            throw new ArgumentException("Objective must contain 1 to 2000 characters.", nameof(command));
        }

        return await this.coordinator.Start(objective, cancellationToken);
    }
}
