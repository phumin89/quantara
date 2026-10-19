using Mediator;

namespace Quantara.Team.Application;

public sealed record GetTeamNewsQuery(string? Symbols) : IQuery<string>;
