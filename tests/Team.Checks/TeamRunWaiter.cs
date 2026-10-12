using Quantara.Team.Domain;

namespace Team.Checks;

public sealed record TeamRunWaiter(Guid Id, Func<TeamRun, bool> Predicate, TaskCompletionSource<TeamRun> Completion);
