using Quantara.Team.Domain;

namespace Quantara.Team.Api;

public sealed record DailyBriefResponse(string Date, IReadOnlyList<TeamRun> Runs);
