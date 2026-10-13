using Mediator;

namespace Quantara.Team.Application;

public sealed class SetCompanyEnabledHandler : ICommandHandler<SetCompanyEnabledCommand, CompanyLoopOverview>
{
    private readonly ICompanyLoop loop;

    public SetCompanyEnabledHandler(ICompanyLoop loop)
    {
        this.loop = loop;
    }

    public async ValueTask<CompanyLoopOverview> Handle(
        SetCompanyEnabledCommand command,
        CancellationToken cancellationToken
    )
    {
        return await this.loop.SetEnabled(command.Enabled, cancellationToken);
    }
}
