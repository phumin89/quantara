using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record StopTeamRunCommand(Guid Id) : ICommand<TeamRun?>;
