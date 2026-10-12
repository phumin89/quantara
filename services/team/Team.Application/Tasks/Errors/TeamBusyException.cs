namespace Quantara.Team.Application;

public sealed class TeamBusyException : InvalidOperationException
{
    public TeamBusyException()
        : base("A team task is already active. Stop it or wait for completion.") { }
}
