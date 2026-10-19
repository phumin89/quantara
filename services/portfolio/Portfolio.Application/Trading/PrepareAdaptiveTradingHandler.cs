using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class PrepareAdaptiveTradingHandler(ITradingControl control, TradingOverviewProjection projection)
    : ICommandHandler<PrepareAdaptiveTradingCommand, string>
{
    public async ValueTask<string> Handle(PrepareAdaptiveTradingCommand command, CancellationToken cancellationToken) =>
        projection.Serialize(await control.PrepareAdaptive(command.AccountKey, command.RulesHash, cancellationToken));
}
