using Mediator;

namespace Quantara.Portfolio.Application;

public sealed class DisableTradingHandler(ITradingControl control, TradingOverviewProjection projection)
    : ICommandHandler<DisableTradingCommand, string>
{
    public async ValueTask<string> Handle(DisableTradingCommand command, CancellationToken cancellationToken) =>
        projection.Serialize(await control.Disable(command.Emergency, cancellationToken));
}
