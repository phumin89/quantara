namespace Quantara.Team.Application;

public sealed class TeamUnavailableException : InvalidOperationException
{
    public TeamUnavailableException()
        : base("Configure the Codex worker authentication and runtime on the server.") { }
}
