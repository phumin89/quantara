using Mediator;
using Quantara.Team.Domain;

namespace Quantara.Team.Application;

public sealed record GetDailyBriefQuery(DateOnly Date) : IQuery<IReadOnlyList<TeamRun>>;
