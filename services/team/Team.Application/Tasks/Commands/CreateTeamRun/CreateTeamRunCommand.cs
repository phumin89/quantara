using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record CreateTeamRunCommand(string Objective) : ICommand<TeamRun>;
