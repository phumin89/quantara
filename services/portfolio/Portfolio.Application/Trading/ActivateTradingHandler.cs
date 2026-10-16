using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class ActivateTradingHandler(ITradingControl control, TradingOverviewProjection projection)
    : ICommandHandler<ActivateTradingCommand, string>
{
    public async ValueTask<string> Handle(ActivateTradingCommand command, CancellationToken cancellationToken) =>
        projection.Serialize(
            await control.Activate(
                command.AccountKey,
                command.RulesHash,
                command.ExpiresAt,
                command.Acknowledgement,
                cancellationToken
            )
        );
}
