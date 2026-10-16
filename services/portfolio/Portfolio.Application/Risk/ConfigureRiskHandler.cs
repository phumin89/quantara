using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class ConfigureRiskHandler(IRiskStore store, IMediator mediator)
    : ICommandHandler<ConfigureRiskCommand, RiskOverview>
{
    public async ValueTask<RiskOverview> Handle(ConfigureRiskCommand command, CancellationToken cancellationToken)
    {
        command.Policy.Validate();
        if (command.ExpectedPolicyHash.Length != 64 || !command.ExpectedPolicyHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Reload the current risk policy before saving.");
        }

        await store.Configure(command.Policy, command.ExpectedPolicyHash, cancellationToken);
        return await mediator.Send(new GetRiskOverviewQuery(), cancellationToken);
    }
}
