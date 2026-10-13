namespace Quantara.Team.Domain;

public enum CompanyLoopStatus
{
    Stopped,
    WaitingForCodex,
    WaitingForPortfolio,
    Running,
    Waiting,
    Error,
}
